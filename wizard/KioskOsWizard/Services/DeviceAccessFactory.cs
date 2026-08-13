using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

public static class DeviceAccessFactory
{
    public static IDeviceAccess Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsDeviceAccess();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxDeviceAccess();

        throw new PlatformNotSupportedException(
            $"Writing sticks is not supported on {RuntimeInformation.OSDescription} yet.");
    }
}

/// <summary>
/// Treats a regular file as the target device. Used by the tests so the write
/// path can be exercised without a real stick.
/// </summary>
public class FileDeviceAccess : IDeviceAccess
{
    private readonly int _sectorSize;

    public FileDeviceAccess(int sectorSize = 512) => _sectorSize = sectorSize;

    public virtual Task PrepareForWritingAsync(string devicePath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Stream OpenWrite(string devicePath) =>
        new FileStream(devicePath, FileMode.Create, FileAccess.Write, FileShare.Read);

    public virtual Stream OpenRead(string devicePath) =>
        new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    public virtual int GetSectorSize(string devicePath) => _sectorSize;

    public virtual Task FinishWritingAsync(string devicePath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual bool HasRequiredPrivileges() => true;
}
