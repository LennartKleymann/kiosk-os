using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using KioskOsWizard.Models;

namespace KioskOsWizard.Services;

/// <summary>
/// macOS implementation using `diskutil list -plist external` to enumerate
/// removable/external disks. Parses the plist output to get device info.
/// </summary>
public class MacOsUsbService : IUsbService
{
    public async Task<IReadOnlyList<UsbDevice>> ListDevicesAsync()
    {
        var devices = new List<UsbDevice>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return devices;

        // Use diskutil JSON-style info per disk
        var listOutput = await RunCommandAsync("diskutil", "list -plist external");
        if (string.IsNullOrWhiteSpace(listOutput))
            return devices;

        // Extract disk identifiers like "disk2" from the plist output
        var identifiers = new List<string>();
        var lines = listOutput.Split('\n');
        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].Contains("DeviceIdentifier") && lines[i + 1].Contains("<string>"))
            {
                var line = lines[i + 1];
                var start = line.IndexOf("<string>") + 8;
                var end = line.IndexOf("</string>");
                if (start > 7 && end > start)
                {
                    var id = line.Substring(start, end - start);
                    // Skip partitions (disk2s1), keep whole disks (disk2)
                    if (!id.Contains('s') || (id.LastIndexOf('s') < 4))
                    {
                        if (!identifiers.Contains(id)) identifiers.Add(id);
                    }
                }
            }
        }

        foreach (var id in identifiers)
        {
            try
            {
                var info = await RunCommandAsync("diskutil", $"info -plist {id}");
                var device = ParseDiskInfo(id, info);
                if (device != null) devices.Add(device);
            }
            catch { /* skip this disk */ }
        }

        return devices;
    }

    private static UsbDevice? ParseDiskInfo(string id, string plist)
    {
        string? model = null;
        long size = 0;
        bool removable = false;

        var lines = plist.Split('\n');
        for (int i = 0; i < lines.Length - 1; i++)
        {
            var line = lines[i].Trim();
            var next = lines[i + 1].Trim();

            if (line.Contains("<key>MediaName</key>") && next.StartsWith("<string>"))
                model = ExtractString(next);
            else if (line.Contains("<key>IORegistryEntryName</key>") && next.StartsWith("<string>"))
                model ??= ExtractString(next);
            else if (line.Contains("<key>TotalSize</key>") && next.StartsWith("<integer>"))
                long.TryParse(ExtractInteger(next), out size);
            else if (line.Contains("<key>RemovableMedia</key>") && next.StartsWith("<true/>"))
                removable = true;
            else if (line.Contains("<key>Removable</key>") && next.StartsWith("<true/>"))
                removable = true;
        }

        if (size == 0) return null;

        return new UsbDevice
        {
            Path = $"/dev/{id}",
            Model = model ?? id,
            SizeBytes = size,
            IsRemovable = removable,
        };
    }

    private static string ExtractString(string line)
    {
        var start = line.IndexOf("<string>") + 8;
        var end = line.IndexOf("</string>");
        return start > 7 && end > start ? line.Substring(start, end - start) : "";
    }

    private static string ExtractInteger(string line)
    {
        var start = line.IndexOf("<integer>") + 9;
        var end = line.IndexOf("</integer>");
        return start > 8 && end > start ? line.Substring(start, end - start) : "0";
    }

    private static async Task<string> RunCommandAsync(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        if (p == null) return "";
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output;
    }
}
