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

    public FlashStepViewModel(KioskConfig config, FlashService flashService) : base(config)
    {
        Title = "Flash the USB stick";
        _flashService = flashService;
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

        IsFlashing = true;
        ErrorMessage = null;
        _cts = new CancellationTokenSource();

        try
        {
            await _flashService.FlashAsync(
                IsoPath,
                DevicePath,
                (written, total) =>
                {
                    var pct = total > 0 ? (double)written / total * 100 : 0;
                    ProgressPercent = pct;
                    ProgressText = $"Writing... {pct:F0}% ({FormatBytes(written)} / {FormatBytes(total)})";
                },
                _cts.Token);

            ProgressText = "Flashing complete!";
            IsDone = true;
            OnPropertyChanged(nameof(CanProceed));
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Cancelled";
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = "Permission denied. Try running the wizard as administrator.";
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

    private static string FormatBytes(long bytes)
    {
        const long MB = 1024 * 1024;
        const long GB = MB * 1024;
        return bytes >= GB
            ? $"{bytes / (double)GB:F2} GB"
            : $"{bytes / (double)MB:F0} MB";
    }
}
