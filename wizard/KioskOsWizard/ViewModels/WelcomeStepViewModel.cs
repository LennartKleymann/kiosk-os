using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KioskOsWizard.Models;
using KioskOsWizard.Services;

namespace KioskOsWizard.ViewModels;

public partial class WelcomeStepViewModel : StepViewModel
{
    private readonly FlashService _flashService;

    [ObservableProperty]
    private bool _hasPrivileges;

    [ObservableProperty]
    private string _privilegeMessage = "";

    public bool CanRestartElevated => !HasPrivileges && OperatingSystem.IsWindows();

    public WelcomeStepViewModel(KioskConfig config, FlashService flashService) : base(config)
    {
        Title = "Welcome";
        _flashService = flashService;
        CheckPrivileges();
    }

    /// <summary>
    /// Checked here rather than at the flashing step: finding out after
    /// configuring everything and picking a stick means doing it all again.
    /// </summary>
    private void CheckPrivileges()
    {
        HasPrivileges = _flashService.HasRequiredPrivileges();

        PrivilegeMessage = HasPrivileges
            ? "Running with the permissions needed to write a USB stick."
            : OperatingSystem.IsWindows()
                ? "Writing to a USB stick needs administrator rights, and this wizard does not have them. Restart it elevated before going any further."
                : "Writing to a USB stick needs root. Quit and start the wizard again with pkexec or sudo.";

        WizardLog.Info($"Privilege check: {(HasPrivileges ? "ok" : "insufficient")}");
        OnPropertyChanged(nameof(CanRestartElevated));
    }

    [RelayCommand]
    private void RestartElevated()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            // UseShellExecute with runas is what raises the UAC prompt.
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            // Most likely the user dismissed the UAC prompt.
            WizardLog.Error("Restarting elevated failed", ex);
            PrivilegeMessage = "Could not restart with administrator rights. " +
                               "Close the wizard and use \"Run as administrator\" from its context menu.";
        }
    }
}
