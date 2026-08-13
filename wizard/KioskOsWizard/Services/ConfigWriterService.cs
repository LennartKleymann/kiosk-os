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
        string content, CancellationToken cancellationToken = default)
    {
        var mountPoint = await WaitForPartitionAsync(cancellationToken);

        if (mountPoint is null)
            throw new IOException(
                $"Could not find the {PartitionLabel} partition on the stick. " +
                "The image may predate the config partition, or the stick was removed.");

        var target = Path.Combine(mountPoint, ConfigFileName);

        // A short line count is the only sanity check available here: a
        // truncated write leaves a kiosk that silently ignores its settings.
        await File.WriteAllTextAsync(target, content, new UTF8Encoding(false), cancellationToken);

        var readBack = await File.ReadAllTextAsync(target, cancellationToken);
        if (readBack.Trim() != content.Trim())
            throw new IOException("The configuration did not survive being written to the stick.");
    }

    private async Task<string?> WaitForPartitionAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + PartitionWait;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mountPoint = await FindMountPointAsync(cancellationToken);
            if (mountPoint is not null)
                return mountPoint;

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return null;
    }

    protected virtual Task<string?> FindMountPointAsync(CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return FindOnWindowsAsync(cancellationToken);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return FindOnLinuxAsync(cancellationToken);

        throw new PlatformNotSupportedException(
            $"Writing the config is not supported on {RuntimeInformation.OSDescription} yet.");
    }

    private static async Task<string?> FindOnWindowsAsync(CancellationToken cancellationToken)
    {
        var script =
            $"Get-Volume -FileSystemLabel {PartitionLabel} -ErrorAction SilentlyContinue | " +
            "Select-Object -ExpandProperty DriveLetter | ConvertTo-Json";

        var output = await RunAsync("powershell", $"-NoProfile -Command \"{script}\"", cancellationToken);
        if (string.IsNullOrWhiteSpace(output)) return null;

        try
        {
            using var doc = JsonDocument.Parse(output.TrimStart().StartsWith('[') ? output : $"[{output}]");
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var letter = e.ValueKind == JsonValueKind.Number
                    ? ((char)e.GetInt32()).ToString()
                    : e.GetString();

                if (!string.IsNullOrWhiteSpace(letter))
                    return $"{letter}:\\";
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
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
