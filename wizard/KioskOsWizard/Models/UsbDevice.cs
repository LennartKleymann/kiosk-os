namespace KioskOsWizard.Models;

/// <summary>
/// A removable USB storage device that can be targeted for flashing.
/// </summary>
public class UsbDevice
{
    public required string Path { get; init; }
    public required string Model { get; init; }
    public required long SizeBytes { get; init; }
    public bool IsRemovable { get; init; }

    public string SizeFormatted
    {
        get
        {
            const long GB = 1024L * 1024 * 1024;
            return SizeBytes >= GB
                ? $"{SizeBytes / (double)GB:F1} GB"
                : $"{SizeBytes / (1024.0 * 1024):F0} MB";
        }
    }

    public string DisplayName => $"{Model} ({SizeFormatted}) — {Path}";
}
