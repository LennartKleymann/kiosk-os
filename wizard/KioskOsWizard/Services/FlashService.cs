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
        await _device.PrepareForWritingAsync(devicePath, cancellationToken);

        var sectorSize = _device.GetSectorSize(devicePath);
        byte[] writtenHash;

        await using (var source = File.OpenRead(isoPath))
        await using (var target = _device.OpenWrite(devicePath))
        {
            writtenHash = await CopyAsync(
                source, target, totalBytes, sectorSize,
                written => progress(new FlashProgress(FlashStage.Writing, written, totalBytes)),
                cancellationToken);

            await target.FlushAsync(cancellationToken);
        }

        await _device.FinishWritingAsync(devicePath, cancellationToken);

        progress(new FlashProgress(FlashStage.Verifying, 0, totalBytes));
        await VerifyAsync(devicePath, totalBytes, writtenHash,
            read => progress(new FlashProgress(FlashStage.Verifying, read, totalBytes)),
            cancellationToken);
    }

    /// <summary>
    /// Copies the image and returns the hash of what was written. Raw device
    /// writes must be a whole number of sectors, so the final chunk — which
    /// almost never lands on a sector boundary — is padded with zeros.
    /// </summary>
    private static async Task<byte[]> CopyAsync(
        Stream source,
        Stream target,
        long totalBytes,
        int sectorSize,
        Action<long> onProgress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize + sectorSize);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long written = 0;

        try
        {
            while (written < totalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken);
                if (read == 0) break;

                hash.AppendData(buffer, 0, read);

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

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Reads the image back off the device and compares it with what was
    /// written. USB sticks fail partially and silently, and a half-written
    /// stick is hard to diagnose once it is in the kiosk.
    /// </summary>
    private async Task VerifyAsync(
        string devicePath,
        long totalBytes,
        byte[] expectedHash,
        Action<long> onProgress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long read = 0;

        try
        {
            await using var device = _device.OpenRead(devicePath);

            while (read < totalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var want = (int)Math.Min(BufferSize, totalBytes - read);
                var got = await device.ReadAsync(buffer.AsMemory(0, want), cancellationToken);
                if (got == 0)
                    throw new IOException($"Device ended after {read} of {totalBytes} bytes");

                hash.AppendData(buffer, 0, got);
                read += got;
                onProgress(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expectedHash))
            throw new IOException(
                "Verification failed: the data read back does not match what was written. " +
                "The stick may be faulty or was removed during writing.");
    }

    private static int RoundUpToSector(int length, int sectorSize) =>
        (length + sectorSize - 1) / sectorSize * sectorSize;
}
