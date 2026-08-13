using System.Text;

namespace KioskOsWizard.Services;

public static class DevicePaths
{
    /// <summary>
    /// Trailing digits of a device path — 2 for \\.\PhysicalDrive2. Plain
    /// string work, so it stays out of the platform-specific classes.
    /// </summary>
    public static int? ExtractDiskNumber(string devicePath)
    {
        var digits = new StringBuilder();
        for (var i = devicePath.Length - 1; i >= 0 && char.IsDigit(devicePath[i]); i--)
            digits.Insert(0, devicePath[i]);

        return digits.Length > 0 && int.TryParse(digits.ToString(), out var n) ? n : null;
    }
}
