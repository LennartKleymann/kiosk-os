using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace KioskOsWizard.Services;

[SupportedOSPlatform("windows")]
public class WindowsDeviceAccess : IDeviceAccess
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlDismountVolume = 0x00090020;
    private const uint IoctlDiskUpdateProperties = 0x00070140;
    private const uint IoctlDiskGetDriveGeometry = 0x00070000;

    private readonly List<SafeFileHandle> _lockedVolumes = new();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskGeometry
    {
        public long Cylinders;
        public uint MediaType;
        public uint TracksPerCylinder;
        public uint SectorsPerTrack;
        public uint BytesPerSector;
    }

    public bool HasRequiredPrivileges()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Locks and dismounts every volume on the disk. Without this the file
    /// system keeps writing to the device while the image is being written,
    /// which corrupts the result in ways that only show up at boot.
    /// </summary>
    public async Task PrepareForWritingAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        ReleaseVolumeLocks();

        foreach (var letter in await GetVolumeLettersAsync(devicePath, cancellationToken))
        {
            var handle = CreateFileW($@"\\.\{letter}:", GenericRead | GenericWrite,
                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

            if (handle.IsInvalid)
            {
                handle.Dispose();
                continue;
            }

            if (!DeviceIoControl(handle, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                handle.Dispose();
                throw new IOException(
                    $"Could not lock volume {letter}: — close any window or program using the stick and try again.");
            }

            DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            _lockedVolumes.Add(handle);
        }
    }

    public Stream OpenWrite(string devicePath)
    {
        var handle = CreateFileW(devicePath, GenericRead | GenericWrite,
            FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException($"Cannot open {devicePath} for writing", Marshal.GetLastWin32Error());

        return new FileStream(handle, FileAccess.ReadWrite);
    }

    public Stream OpenRead(string devicePath)
    {
        var handle = CreateFileW(devicePath, GenericRead,
            FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException($"Cannot open {devicePath} for reading", Marshal.GetLastWin32Error());

        return new FileStream(handle, FileAccess.Read);
    }

    public int GetSectorSize(string devicePath)
    {
        using var handle = CreateFileW(devicePath, GenericRead,
            FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            return 512;

        var size = Marshal.SizeOf<DiskGeometry>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (DeviceIoControl(handle, IoctlDiskGetDriveGeometry, IntPtr.Zero, 0,
                    buffer, (uint)size, out _, IntPtr.Zero))
            {
                var geometry = Marshal.PtrToStructure<DiskGeometry>(buffer);
                if (geometry.BytesPerSector > 0)
                    return (int)geometry.BytesPerSector;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return 512;
    }

    public Task FinishWritingAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        ReleaseVolumeLocks();

        using var handle = CreateFileW(devicePath, GenericRead | GenericWrite,
            FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (!handle.IsInvalid)
            DeviceIoControl(handle, IoctlDiskUpdateProperties, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

        return Task.CompletedTask;
    }

    private void ReleaseVolumeLocks()
    {
        foreach (var handle in _lockedVolumes)
            handle.Dispose();
        _lockedVolumes.Clear();
    }

    private static async Task<IReadOnlyList<string>> GetVolumeLettersAsync(
        string devicePath, CancellationToken cancellationToken)
    {
        var diskNumber = DevicePaths.ExtractDiskNumber(devicePath);
        if (diskNumber is null) return Array.Empty<string>();

        var script =
            $"Get-Partition -DiskNumber {diskNumber} | " +
            "Where-Object { $_.DriveLetter } | Select-Object -ExpandProperty DriveLetter | ConvertTo-Json";

        var output = await RunPowerShellAsync(script, cancellationToken);
        if (string.IsNullOrWhiteSpace(output)) return Array.Empty<string>();

        var letters = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(output.TrimStart().StartsWith('[') ? output : $"[{output}]");
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var letter = e.ValueKind == JsonValueKind.Number
                    ? ((char)e.GetInt32()).ToString()
                    : e.GetString();
                if (!string.IsNullOrWhiteSpace(letter))
                    letters.Add(letter!);
            }
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }

        return letters;
    }

    private static async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{script}\"")
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
        return output;
    }
}
