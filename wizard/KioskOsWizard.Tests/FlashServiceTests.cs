using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KioskOsWizard.Services;
using Xunit;

namespace KioskOsWizard.Tests;

public class FlashServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kiosk-flash-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CreateImage(long length)
    {
        var path = Path.Combine(_dir, $"image-{length}.iso");
        var data = new byte[length];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    private string TargetPath => Path.Combine(_dir, "device.img");

    /// <summary>
    /// The original implementation wrote whatever the last read returned,
    /// which is almost never a multiple of the sector size. Raw device writes
    /// reject that, so every flash failed at the very end.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(511)]
    [InlineData(513)]
    [InlineData(4 * 1024 * 1024 + 1)]
    [InlineData(4 * 1024 * 1024 + 4095)]
    public async Task Writes_images_whose_length_is_not_a_multiple_of_the_sector_size(long length)
    {
        var iso = CreateImage(length);
        var service = new FlashService(new FileDeviceAccess(sectorSize: 512));

        await service.FlashAsync(iso, TargetPath, _ => { });

        var written = File.ReadAllBytes(TargetPath);
        var expected = File.ReadAllBytes(iso);

        Assert.True(written.Length >= expected.Length);
        Assert.Equal(expected, written[..expected.Length]);

        // Padding must be zeros, not leftover buffer contents.
        for (var i = expected.Length; i < written.Length; i++)
            Assert.Equal(0, written[i]);
    }

    [Fact]
    public async Task Reports_progress_through_writing_and_verifying()
    {
        var iso = CreateImage(3 * 1024 * 1024);
        var service = new FlashService(new FileDeviceAccess());
        var stages = new List<FlashStage>();
        long lastWritten = 0;

        await service.FlashAsync(iso, TargetPath, p =>
        {
            stages.Add(p.Stage);
            if (p.Stage == FlashStage.Writing)
            {
                Assert.True(p.Current >= lastWritten, "progress went backwards");
                lastWritten = p.Current;
            }
        });

        Assert.Contains(FlashStage.Preparing, stages);
        Assert.Contains(FlashStage.Writing, stages);
        Assert.Contains(FlashStage.Verifying, stages);
        Assert.Equal(new FileInfo(iso).Length, lastWritten);
    }

    [Fact]
    public async Task Verification_fails_when_the_device_did_not_store_what_was_written()
    {
        var iso = CreateImage(2 * 1024 * 1024);
        var service = new FlashService(new CorruptingDeviceAccess());

        var ex = await Assert.ThrowsAsync<IOException>(
            () => service.FlashAsync(iso, TargetPath, _ => { }));

        Assert.Contains("Verification failed", ex.Message);
    }

    [Fact]
    public async Task Cancellation_stops_the_write()
    {
        var iso = CreateImage(64 * 1024 * 1024);
        var service = new FlashService(new FileDeviceAccess());
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.FlashAsync(iso, TargetPath, p =>
            {
                if (p.Stage == FlashStage.Writing && p.Current > 0)
                    cts.Cancel();
            }, cts.Token));
    }

    [Fact]
    public async Task Missing_image_is_reported_before_the_device_is_touched()
    {
        var service = new FlashService(new FileDeviceAccess());

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.FlashAsync(Path.Combine(_dir, "nope.iso"), TargetPath, _ => { }));

        Assert.False(File.Exists(TargetPath));
    }

    /// <summary>Stores one flipped bit, the way a failing stick would.</summary>
    private class CorruptingDeviceAccess : FileDeviceAccess
    {
        public override Stream OpenRead(string devicePath)
        {
            var data = File.ReadAllBytes(devicePath);
            data[0] ^= 0xFF;
            return new MemoryStream(data);
        }
    }
}
