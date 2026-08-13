using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

/// <summary>
/// Writes an ISO image directly to a USB device using raw block I/O.
/// Reports progress via the callback (bytesWritten, totalBytes).
/// </summary>
public class FlashService
{
    public async Task FlashAsync(
        string isoPath,
        string devicePath,
        Action<long, long> progress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(isoPath))
            throw new FileNotFoundException("ISO file not found", isoPath);

        var totalBytes = new FileInfo(isoPath).Length;
        const int bufferSize = 4 * 1024 * 1024; // 4 MB chunks
        var buffer = new byte[bufferSize];
        long writtenBytes = 0;

        await using var source = File.OpenRead(isoPath);
        await using var target = new FileStream(
            devicePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.WriteThrough);

        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            writtenBytes += read;
            progress(writtenBytes, totalBytes);
        }

        await target.FlushAsync(cancellationToken);
    }
}
