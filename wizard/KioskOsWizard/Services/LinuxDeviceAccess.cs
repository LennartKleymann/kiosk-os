using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

[SupportedOSPlatform("linux")]
public class LinuxDeviceAccess : IDeviceAccess
{
    public bool HasRequiredPrivileges() => Environment.IsPrivilegedProcess;

    /// <summary>
    /// Unmounts every partition of the device. udisksctl is preferred because
    /// it works without root for user-mounted sticks; umount is the fallback.
    /// </summary>
    public async Task PrepareForWritingAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        foreach (var partition in EnumeratePartitions(devicePath))
        {
            if (await TryRunAsync("udisksctl", $"unmount -b {partition} --no-user-interaction", cancellationToken))
                continue;

            await TryRunAsync("umount", partition, cancellationToken);
        }
    }

    public Stream OpenWrite(string devicePath) =>
        new FileStream(devicePath, FileMode.Open, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);

    public Stream OpenRead(string devicePath) =>
        new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    public int GetSectorSize(string devicePath)
    {
        var name = Path.GetFileName(devicePath);
        var sysfs = $"/sys/block/{name}/queue/logical_block_size";

        if (File.Exists(sysfs) && int.TryParse(File.ReadAllText(sysfs).Trim(), out var size) && size > 0)
            return size;

        return 512;
    }

    public async Task FinishWritingAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        await TryRunAsync("sync", string.Empty, cancellationToken);
        if (!await TryRunAsync("partprobe", devicePath, cancellationToken))
            await TryRunAsync("blockdev", $"--rereadpt {devicePath}", cancellationToken);

        // Give udev a moment to create the new partition nodes before the
        // caller goes looking for the config partition.
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private static string[] EnumeratePartitions(string devicePath)
    {
        var name = Path.GetFileName(devicePath);
        var blockDir = $"/sys/block/{name}";

        if (!Directory.Exists(blockDir))
            return Array.Empty<string>();

        var partitions = new System.Collections.Generic.List<string>();
        foreach (var dir in Directory.GetDirectories(blockDir, $"{name}*"))
            partitions.Add($"/dev/{Path.GetFileName(dir)}");

        return partitions.ToArray();
    }

    private static async Task<bool> TryRunAsync(string command, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
