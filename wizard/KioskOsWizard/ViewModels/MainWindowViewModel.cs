using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KioskOsWizard.Models;
using KioskOsWizard.Services;

namespace KioskOsWizard.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly KioskConfig _config = new();
    private readonly List<StepViewModel> _steps;
    private int _currentIndex;

    [ObservableProperty]
    private StepViewModel _currentStep;

    [ObservableProperty]
    private string _statusMessage = "";

    public int TotalSteps => _steps.Count;
    public int CurrentStepNumber => _currentIndex + 1;
    public bool CanGoBack => _currentIndex > 0;
    public bool CanGoNext => CurrentStep.CanProceed;
    public string NextButtonText => _currentIndex == _steps.Count - 1 ? "Finish" : "Next";

    public MainWindowViewModel()
    {
        var usbService = UsbServiceFactory.Create();
        var flashService = new FlashService();
        var githubService = new GithubReleaseService();

        _steps = new List<StepViewModel>
        {
            new WelcomeStepViewModel(_config),
            new ConfigStepViewModel(_config),
            new UsbStepViewModel(_config, usbService, githubService),
            new FlashStepViewModel(_config, flashService),
        };

        _currentStep = _steps[0];
        _currentStep.OnEntered();
        HookStepProperties();
    }

    [RelayCommand]
    private void Next()
    {
        CurrentStep.OnLeaving();

        if (_currentIndex == _steps.Count - 1)
        {
            // Finish — close the app or show final screen
            return;
        }

        _currentIndex++;
        CurrentStep = _steps[_currentIndex];
        CurrentStep.OnEntered();

        // Special handling: when entering flash step, prepare with selected device + ISO
        if (CurrentStep is FlashStepViewModel flash)
        {
            var usb = (UsbStepViewModel)_steps[2];
            if (usb.SelectedDevice != null)
                flash.Prepare(usb.SelectedDevice, usb.ResolvedIsoPath);
        }

        NotifyNavigation();
        HookStepProperties();
    }

    [RelayCommand]
    private void Back()
    {
        if (_currentIndex == 0) return;
        _currentIndex--;
        CurrentStep = _steps[_currentIndex];
        CurrentStep.OnEntered();
        NotifyNavigation();
        HookStepProperties();
    }

    private void HookStepProperties()
    {
        CurrentStep.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StepViewModel.CanProceed) || e.PropertyName == null)
                OnPropertyChanged(nameof(CanGoNext));
        };
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CurrentStepNumber));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextButtonText));
    }
}
