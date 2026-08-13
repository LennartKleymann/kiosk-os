using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

/// <summary>
/// Writes kiosk.conf onto the KIOSK_CFG partition that the image carries.
/// The kiosk reads that partition on every boot, so the same stick can be
/// reconfigured later by editing the file on any PC.
/// </summary>
public class ConfigWriterService
{
    public const string PartitionLabel = "KIOSK_CFG";
    public const string ConfigFileName = "kiosk.conf";

    private static readonly TimeSpan PartitionWait = TimeSpan.FromSeconds(20);

    public virtual async Task WriteAsync(
        string content, string? devicePath = null, CancellationToken cancellationToken = default)
    {
        var mountPoint = await WaitForPartitionAsync(devicePath, cancellationToken);

        if (mountPoint is null)
            throw new IOException(
                $"Could not reach the {PartitionLabel} partition on the stick. " +
                "It is present on the image, but the system did not make it accessible. " +
                "Unplugging and reinserting the stick usually fixes this; the configuration " +
                "can also be copied onto that partition by hand as kiosk.conf.");

        var target = Path.Combine(mountPoint, ConfigFileName);

        // A short line count is the only sanity check available here: a
        // truncated write leaves a kiosk that silently ignores its settings.
        await File.WriteAllTextAsync(target, content, new UTF8Encoding(false), cancellationToken);

        var readBack = await File.ReadAllTextAsync(target, cancellationToken);
        if (readBack.Trim() != content.Trim())
            throw new IOException("The configuration did not survive being written to the stick.");
    }

    private async Task<string?> WaitForPartitionAsync(string? devicePath, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + PartitionWait;
        var assigned = false;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mountPoint = await FindMountPointAsync(devicePath, cancellationToken);
            if (mountPoint is not null)
            {
                WizardLog.Info($"Config partition reachable at {mountPoint}");
                return mountPoint;
            }

            // Windows does not always hand a drive letter to every partition
            // on a removable disk. Ask for one once, then keep waiting.
            if (!assigned && devicePath is not null && OperatingSystem.IsWindows())
            {
                assigned = true;
                await AssignDriveLetterAsync(devicePath, cancellationToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return null;
    }

    protected virtual Task<string?> FindMountPointAsync(string? devicePath, CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return FindOnWindowsAsync(devicePath, cancellationToken);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return FindOnLinuxAsync(cancellationToken);

        throw new PlatformNotSupportedException(
            $"Writing the config is not supported on {RuntimeInformation.OSDescription} yet.");
    }

    /// <summary>
    /// Prefers the volume GUID path over a drive letter. The GUID path always
    /// exists once the volume is known, survives letter reassignment, and does
    /// not require changing anything about the user's system. The letter is
    /// only a fallback, and worth logging because it is what the user can
    /// actually open in Explorer afterwards.
    ///
    /// Scoping to the disk that was just written avoids picking up a second
    /// kiosk stick that happens to be plugged in.
    /// </summary>
    private static async Task<string?> FindOnWindowsAsync(string? devicePath, CancellationToken cancellationToken)
    {
        var diskNumber = devicePath is null ? null : DevicePaths.ExtractDiskNumber(devicePath);

        var source = diskNumber is null
            ? $"Get-Volume -FileSystemLabel {PartitionLabel} -ErrorAction SilentlyContinue"
            : $"Get-Partition -DiskNumber {diskNumber} -ErrorAction SilentlyContinue | Get-Volume | " +
              $"Where-Object {{ $_.FileSystemLabel -eq '{PartitionLabel}' }}";

        var script = source + " | Select-Object -First 1 -Property DriveLetter,Path | ConvertTo-Json";

        var output = await RunAsync("powershell", $"-NoProfile -Command \"{script}\"", cancellationToken);
        if (string.IsNullOrWhiteSpace(output)) return null;

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement[0]
                : doc.RootElement;

            string? letter = null;
            if (root.TryGetProperty("DriveLetter", out var dl) && dl.ValueKind != JsonValueKind.Null)
            {
                letter = dl.ValueKind == JsonValueKind.Number
                    ? ((char)dl.GetInt32()).ToString()
                    : dl.GetString();
            }

            if (root.TryGetProperty("Path", out var p) && p.ValueKind == JsonValueKind.String)
            {
                var path = p.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    if (!string.IsNullOrWhiteSpace(letter))
                        WizardLog.Info($"Config partition is also mounted as {letter}:");
                    return path;
                }
            }

            if (!string.IsNullOrWhiteSpace(letter))
                return $"{letter}:\\";
        }
        catch (Exception ex) when (ex is JsonException or IndexOutOfRangeException)
        {
            return null;
        }

        return null;
    }

    private static async Task AssignDriveLetterAsync(string devicePath, CancellationToken cancellationToken)
    {
        var diskNumber = DevicePaths.ExtractDiskNumber(devicePath);
        if (diskNumber is null) return;

        WizardLog.Info($"No drive letter for {PartitionLabel}, requesting one on disk {diskNumber}");

        var script =
            $"$p = Get-Partition -DiskNumber {diskNumber} | " +
            $"Where-Object {{ ($_ | Get-Volume -ErrorAction SilentlyContinue).FileSystemLabel -eq '{PartitionLabel}' }}; " +
            "if ($p) { $p | Add-PartitionAccessPath -AssignDriveLetter -ErrorAction SilentlyContinue }";

        await RunAsync("powershell", $"-NoProfile -Command \"{script}\"", cancellationToken);
    }

    private static async Task<string?> FindOnLinuxAsync(CancellationToken cancellationToken)
    {
        var device = $"/dev/disk/by-label/{PartitionLabel}";
        if (!File.Exists(device)) return null;

        var mounted = await RunAsync("findmnt", $"-n -o TARGET {device}", cancellationToken);
        if (!string.IsNullOrWhiteSpace(mounted))
            return mounted.Trim();

        // udisksctl prints "Mounted /dev/sdb3 at /run/media/user/KIOSK_CFG."
        var output = await RunAsync(
            "udisksctl", $"mount -b {device} --no-user-interaction", cancellationToken);

        var marker = " at ";
        var index = output.LastIndexOf(marker, StringComparison.Ordinal);
        if (index >= 0)
            return output[(index + marker.Length)..].Trim().TrimEnd('.');

        mounted = await RunAsync("findmnt", $"-n -o TARGET {device}", cancellationToken);
        return string.IsNullOrWhiteSpace(mounted) ? null : mounted.Trim();
    }

    private static async Task<string> RunAsync(string command, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
