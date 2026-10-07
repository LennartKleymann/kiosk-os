using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using KioskOsWizard.Models;

namespace KioskOsWizard.Services;

/// <summary>
/// Windows implementation using PowerShell Get-Disk + Get-PhysicalDisk
/// to enumerate removable storage devices.
/// </summary>
public class WindowsUsbService : IUsbService
{
    public async Task<IReadOnlyList<UsbDevice>> ListDevicesAsync()
    {
        var devices = new List<UsbDevice>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return devices;

        var ps = "Get-PhysicalDisk | Where-Object { $_.BusType -eq 'USB' } | " +
                 "Select-Object DeviceId, FriendlyName, Size, MediaType | ConvertTo-Json";

        var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{ps}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return devices;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (string.IsNullOrWhiteSpace(output)) return devices;

        // ConvertTo-Json may emit a single object or array — normalize
        if (!output.TrimStart().StartsWith('['))
            output = "[" + output + "]";

        using var doc = JsonDocument.Parse(output);
        foreach (var d in doc.RootElement.EnumerateArray())
        {
            var deviceId = d.TryGetProperty("DeviceId", out var id) ? id.ToString() : "0";
            var name = d.TryGetProperty("FriendlyName", out var n) ? n.GetString() ?? "USB Device" : "USB Device";
            var size = d.TryGetProperty("Size", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt64()
                : 0;

            devices.Add(new UsbDevice
            {
                Path = $@"\\.\PhysicalDrive{deviceId}",
                Model = name,
                SizeBytes = size,
                IsRemovable = true,
            });
        }

        return devices;
    }
}
