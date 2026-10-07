using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KioskOsWizard.Models;
using KioskOsWizard.Services;

namespace KioskOsWizard.ViewModels;

public partial class UsbStepViewModel : StepViewModel
{
    private readonly IUsbService _usbService;
    private readonly GithubReleaseService _githubService;
    private readonly FlashService _flashService;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    private ObservableCollection<UsbDevice> _devices = new();

    [ObservableProperty]
    private UsbDevice? _selectedDevice;

    [ObservableProperty]
    private bool _isScanning;

    // Image source
    [ObservableProperty]
    private bool _useLatestRelease = true;

    [ObservableProperty]
    private string _customIsoPath = "";

    [ObservableProperty]
    private KioskOsRelease? _latestRelease;

    [ObservableProperty]
    private string _releaseStatus = "Fetching latest release...";

    // Download state
    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadPercent;

    [ObservableProperty]
    private string _downloadStatus = "";

    [ObservableProperty]
    private string? _cachedIsoPath;

    [ObservableProperty]
    private string? _deviceWarning;

    public UsbStepViewModel(
        KioskConfig config,
        IUsbService usbService,
        GithubReleaseService githubService,
        FlashService flashService) : base(config)
    {
        Title = "Select image and USB stick";
        _usbService = usbService;
        _githubService = githubService;
        _flashService = flashService;
    }

    /// <summary>
    /// Probes the device as soon as it is picked, so a permission problem
    /// surfaces here rather than after the image has been downloaded.
    /// </summary>
    partial void OnSelectedDeviceChanged(UsbDevice? value)
    {
        DeviceWarning = value is null ? null : _flashService.CheckWritable(value.Path);
        OnPropertyChanged(nameof(CanProceed));
    }

    public string ResolvedIsoPath =>
        UseLatestRelease ? (CachedIsoPath ?? "") : CustomIsoPath;

    public override bool CanProceed =>
        SelectedDevice != null && DeviceWarning is null
        && !string.IsNullOrEmpty(ResolvedIsoPath) && File.Exists(ResolvedIsoPath);

    public override async void OnEntered()
    {
        _ = FetchLatestReleaseAsync();
        await RefreshUsbAsync();
    }

    [RelayCommand]
    public async Task RefreshUsbAsync()
    {
        IsScanning = true;
        try
        {
            var list = await _usbService.ListDevicesAsync();
            Devices.Clear();
            foreach (var d in list) Devices.Add(d);
            if (SelectedDevice == null && Devices.Count > 0)
                SelectedDevice = Devices[0];
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public async Task FetchLatestReleaseAsync()
    {
        try
        {
            ReleaseStatus = "Fetching latest release...";
            var release = await _githubService.GetLatestReleaseAsync();
            if (release == null)
            {
                ReleaseStatus = "No release available yet";
                return;
            }
            LatestRelease = release;
            ReleaseStatus = $"{release.Name} ({FormatBytes(release.Size)})";

            // Check if we already have it cached
            var cachedPath = Path.Combine(
                GithubReleaseService.CacheDirectory, $"kiosk-os-{release.TagName}.iso");
            if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length == release.Size)
            {
                CachedIsoPath = cachedPath;
                DownloadStatus = "Already downloaded";
                OnPropertyChanged(nameof(CanProceed));
            }
        }
        catch (Exception ex)
        {
            ReleaseStatus = $"Failed to fetch: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DownloadLatestAsync()
    {
        if (LatestRelease == null) return;

        IsDownloading = true;
        _downloadCts = new CancellationTokenSource();
        try
        {
            var path = await _githubService.GetOrDownloadAsync(
                LatestRelease,
                (written, total) =>
                {
                    var pct = total > 0 ? (double)written / total * 100 : 0;
                    DownloadPercent = pct;
                    DownloadStatus = $"Downloading... {pct:F0}% ({FormatBytes(written)} / {FormatBytes(total)})";
                },
                _downloadCts.Token);

            CachedIsoPath = path;
            DownloadStatus = "Download complete and verified";
            OnPropertyChanged(nameof(CanProceed));
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Download cancelled";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        const long MB = 1024 * 1024;
        const long GB = MB * 1024;
        return bytes >= GB
            ? $"{bytes / (double)GB:F2} GB"
            : $"{bytes / (double)MB:F0} MB";
    }

    partial void OnCustomIsoPathChanged(string value) => OnPropertyChanged(nameof(CanProceed));
    partial void OnUseLatestReleaseChanged(bool value) => OnPropertyChanged(nameof(CanProceed));
    partial void OnCachedIsoPathChanged(string? value) => OnPropertyChanged(nameof(CanProceed));
}
