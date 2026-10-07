using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

/// <summary>
/// Platform-specific access to a raw block device. Writing an image to a stick
/// needs more than opening a file: the OS has to be told to let go of the
/// device first and to re-read the partition table afterwards.
/// </summary>
public interface IDeviceAccess
{
    /// <summary>Unmounts and locks every volume on the device.</summary>
    Task PrepareForWritingAsync(string devicePath, CancellationToken cancellationToken = default);

    Stream OpenWrite(string devicePath);

    Stream OpenRead(string devicePath);

    /// <summary>Sector size in bytes; raw writes must be a multiple of it.</summary>
    int GetSectorSize(string devicePath);

    /// <summary>Makes the freshly written partitions visible to the OS.</summary>
    Task FinishWritingAsync(string devicePath, CancellationToken cancellationToken = default);

    /// <summary>Whether the current process may write to raw devices at all.</summary>
    bool HasRequiredPrivileges();
}
