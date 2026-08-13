using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KioskOsWizard.Models;
using KioskOsWizard.Services;

namespace KioskOsWizard.ViewModels;

public partial class FlashStepViewModel : StepViewModel
{
    private readonly FlashService _flashService;
    private readonly ConfigWriterService _configWriter;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isFlashing;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = "Ready to start";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _devicePath = "";

    [ObservableProperty]
    private string _deviceModel = "";

    [ObservableProperty]
    private string _isoPath = "";

    public FlashStepViewModel(
        KioskConfig config,
        FlashService flashService,
        ConfigWriterService configWriter) : base(config)
    {
        Title = "Flash the USB stick";
        _flashService = flashService;
        _configWriter = configWriter;
    }

    public override bool CanProceed => IsDone;

    public void Prepare(UsbDevice device, string isoPath)
    {
        DevicePath = device.Path;
        DeviceModel = $"{device.Model} ({device.SizeFormatted})";
        IsoPath = isoPath;
        IsDone = false;
        IsFlashing = false;
        ProgressPercent = 0;
        ProgressText = "Ready to start";
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task StartFlashAsync()
    {
        if (IsFlashing) return;

        if (!_flashService.HasRequiredPrivileges())
        {
            ErrorMessage = PrivilegeHint;
            return;
        }

        IsFlashing = true;
        ErrorMessage = null;
        _cts = new CancellationTokenSource();

        try
        {
            await _flashService.FlashAsync(IsoPath, DevicePath, Report, _cts.Token);

            ProgressText = "Writing the configuration...";
            await _configWriter.WriteAsync(Config.ToConfigFileContent(), _cts.Token);

            ProgressText = "Done — the stick is ready.";
            IsDone = true;
            OnPropertyChanged(nameof(CanProceed));
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Cancelled";
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = PrivilegeHint;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Flashing failed: {ex.Message}";
        }
        finally
        {
            IsFlashing = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private static string PrivilegeHint => OperatingSystem.IsWindows()
        ? "Writing to a USB stick needs administrator rights. Close the wizard and start it again with \"Run as administrator\"."
        : "Writing to a USB stick needs root. Start the wizard with pkexec or sudo.";

    private void Report(FlashProgress p)
    {
        var pct = p.Total > 0 ? (double)p.Current / p.Total * 100 : 0;
        ProgressPercent = pct;
        ProgressText = p.Stage switch
        {
            FlashStage.Preparing => "Preparing the device...",
            FlashStage.Writing => $"Writing... {pct:F0}% ({FormatBytes(p.Current)} / {FormatBytes(p.Total)})",
            FlashStage.Verifying => $"Verifying... {pct:F0}%",
            _ => ProgressText,
        };
    }

    private static string FormatBytes(long bytes)
    {
        const long MB = 1024 * 1024;
        const long GB = MB * 1024;
        return bytes >= GB
            ? $"{bytes / (double)GB:F2} GB"
            : $"{bytes / (double)MB:F0} MB";
    }
}
