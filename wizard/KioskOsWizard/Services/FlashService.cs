using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

public enum FlashStage
{
    Preparing,
    Writing,
    Verifying,
}

public record FlashProgress(FlashStage Stage, long Current, long Total);

/// <summary>
/// Writes an ISO image directly to a USB device using raw block I/O,
/// then reads it back to confirm the device actually stored it.
/// </summary>
public class FlashService
{
    private const int BufferSize = 4 * 1024 * 1024;

    private readonly Lazy<IDeviceAccess> _deviceAccess;

    private IDeviceAccess _device => _deviceAccess.Value;

    /// <summary>
    /// Device access is resolved on first use, not in the constructor —
    /// on an unsupported platform the wizard should still start and explain
    /// itself rather than crash on launch.
    /// </summary>
    public FlashService(IDeviceAccess? device = null)
    {
        _deviceAccess = device is null
            ? new Lazy<IDeviceAccess>(DeviceAccessFactory.Create)
            : new Lazy<IDeviceAccess>(() => device);
    }

    public bool HasRequiredPrivileges()
    {
        try
        {
            return _device.HasRequiredPrivileges();
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the device for writing and closes it again without writing
    /// anything. Being an administrator does not guarantee this particular
    /// device can be opened, and finding that out after the configuration is
    /// done and the image is downloaded is the worst possible moment.
    /// Returns null when the device is writable, otherwise the reason.
    /// </summary>
    public string? CheckWritable(string devicePath)
    {
        try
        {
            using var probe = _device.OpenWrite(devicePath);
            WizardLog.Info($"Write probe on {devicePath} succeeded");
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            WizardLog.Error($"Write probe on {devicePath}: access denied");
            return OperatingSystem.IsWindows()
                ? "Access denied. Restart the wizard as administrator."
                : "Access denied. Start the wizard with pkexec or sudo.";
        }
        catch (Exception ex)
        {
            WizardLog.Error($"Write probe on {devicePath} failed", ex);
            return ex.Message;
        }
    }

    public async Task FlashAsync(
        string isoPath,
        string devicePath,
        Action<FlashProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(isoPath))
            throw new FileNotFoundException("ISO file not found", isoPath);

        var totalBytes = new FileInfo(isoPath).Length;

        progress(new FlashProgress(FlashStage.Preparing, 0, totalBytes));
        WizardLog.Info($"Flashing {isoPath} ({totalBytes} bytes) to {devicePath}");

        await _device.PrepareForWritingAsync(devicePath, cancellationToken);

        var sectorSize = _device.GetSectorSize(devicePath);
        WizardLog.Info($"Device released, sector size {sectorSize} bytes");

        await using (var source = File.OpenRead(isoPath))
        await using (var target = _device.OpenWrite(devicePath))
        {
            await CopyAsync(
                source, target, totalBytes, sectorSize,
                written => progress(new FlashProgress(FlashStage.Writing, written, totalBytes)),
                cancellationToken);

            await target.FlushAsync(cancellationToken);
        }

        // Verify while the volumes are still locked. Once the device is
        // released the OS mounts the partitions we just wrote and starts
        // writing its own metadata to them, which would fail the comparison
        // even though the write itself was fine.
        WizardLog.Info("Write complete, verifying");
        progress(new FlashProgress(FlashStage.Verifying, 0, totalBytes));
        await VerifyAsync(isoPath, devicePath, totalBytes,
            read => progress(new FlashProgress(FlashStage.Verifying, read, totalBytes)),
            cancellationToken);

        WizardLog.Info("Verified, releasing device");
        await _device.FinishWritingAsync(devicePath, cancellationToken);
    }

    /// <summary>
    /// Copies the image to the device. Raw device writes must be a whole
    /// number of sectors, so the final chunk — which almost never lands on a
    /// sector boundary — is padded with zeros.
    /// </summary>
    private static async Task CopyAsync(
        Stream source,
        Stream target,
        long totalBytes,
        int sectorSize,
        Action<long> onProgress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize + sectorSize);
        long written = 0;

        try
        {
            while (written < totalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken);
                if (read == 0) break;

                var toWrite = RoundUpToSector(read, sectorSize);
                if (toWrite > read)
                    Array.Clear(buffer, read, toWrite - read);

                await target.WriteAsync(buffer.AsMemory(0, toWrite), cancellationToken);

                written += read;
                onProgress(written);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (written != totalBytes)
            throw new IOException($"Wrote {written} of {totalBytes} bytes");
    }

    /// <summary>
    /// Reads the image back off the device and compares it with the source.
    /// USB sticks fail partially and silently, and a half-written stick is
    /// hard to diagnose once it is in the kiosk. Comparing block by block
    /// rather than by hash means a mismatch can name the offset it occurred
    /// at, which is what distinguishes a bad stick from interference.
    /// </summary>
    private async Task VerifyAsync(
        string isoPath,
        string devicePath,
        long totalBytes,
        Action<long> onProgress,
        CancellationToken cancellationToken)
    {
        var fromDevice = ArrayPool<byte>.Shared.Rent(BufferSize);
        var fromSource = ArrayPool<byte>.Shared.Rent(BufferSize);
        long position = 0;

        try
        {
            await using var device = _device.OpenRead(devicePath);
            await using var source = File.OpenRead(isoPath);

            while (position < totalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var want = (int)Math.Min(BufferSize, totalBytes - position);

                var got = await ReadExactlyAsync(device, fromDevice, want, cancellationToken);
                if (got < want)
                    throw new IOException(
                        $"The device returned only {position + got} of {totalBytes} bytes. " +
                        "It may be smaller than the image or have been removed.");

                await ReadExactlyAsync(source, fromSource, want, cancellationToken);

                var offset = FirstDifference(
                    fromDevice.AsSpan(0, want), fromSource.AsSpan(0, want));

                if (offset >= 0)
                    throw new IOException(
                        $"Verification failed at byte {position + offset} of {totalBytes}. " +
                        (position + offset < 1024 * 1024
                            ? "The mismatch is at the very start of the device, which usually means "
                              + "something else wrote to the stick during the process — close any "
                              + "window showing it and try again."
                            : "The stick may be faulty, counterfeit, or was removed while writing."));

                position += want;
                onProgress(position);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(fromDevice);
            ArrayPool<byte>.Shared.Return(fromSource);
        }
    }

    private static async Task<int> ReadExactlyAsync(
        Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static int FirstDifference(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return i;
        return -1;
    }

    private static int RoundUpToSector(int length, int sectorSize) =>
        (length + sectorSize - 1) / sectorSize * sectorSize;
}
