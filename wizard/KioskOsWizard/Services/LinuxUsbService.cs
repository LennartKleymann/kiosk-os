using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using KioskOsWizard.Models;

namespace KioskOsWizard.Services;

/// <summary>
/// Linux implementation using `lsblk -J -o NAME,SIZE,MODEL,TYPE,RM,PATH -b -d`
/// to enumerate removable block devices.
/// </summary>
public class LinuxUsbService : IUsbService
{
    public async Task<IReadOnlyList<UsbDevice>> ListDevicesAsync()
    {
        var devices = new List<UsbDevice>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return devices;

        var psi = new ProcessStartInfo("lsblk", "-J -o NAME,SIZE,MODEL,TYPE,RM,PATH -b -d")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        if (p == null) return devices;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();

        using var doc = JsonDocument.Parse(output);
        foreach (var d in doc.RootElement.GetProperty("blockdevices").EnumerateArray())
        {
            if (!d.TryGetProperty("type", out var type) || type.GetString() != "disk")
                continue;

            // Only show removable devices by default
            var removable = d.TryGetProperty("rm", out var rm) && rm.GetBoolean();
            if (!removable) continue;

            var path = d.TryGetProperty("path", out var p2) ? p2.GetString() : null;
            if (string.IsNullOrEmpty(path)) continue;

            var model = d.TryGetProperty("model", out var m) ? m.GetString() ?? path : path;
            var size = d.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt64()
                : 0;

            devices.Add(new UsbDevice
            {
                Path = path,
                Model = model.Trim(),
                SizeBytes = size,
                IsRemovable = removable,
            });
        }

        return devices;
    }
}
