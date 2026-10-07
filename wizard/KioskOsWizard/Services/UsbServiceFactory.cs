using System;
using System.Runtime.InteropServices;

namespace KioskOsWizard.Services;

public static class UsbServiceFactory
{
    public static IUsbService Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacOsUsbService();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxUsbService();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsUsbService();

        throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
    }
}
