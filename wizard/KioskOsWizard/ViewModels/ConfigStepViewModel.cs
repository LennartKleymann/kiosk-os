using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using KioskOsWizard.Models;

namespace KioskOsWizard.ViewModels;

public partial class ConfigStepViewModel : StepViewModel
{
    [ObservableProperty]
    private string _homepage;

    [ObservableProperty]
    private bool _useWifi;

    [ObservableProperty]
    private string _wifiSsid = "";

    [ObservableProperty]
    private string _wifiPassword = "";

    [ObservableProperty]
    private string _whitelist = "";

    [ObservableProperty]
    private bool _browserModeKiosk = true;

    [ObservableProperty]
    private string _timezone;

    public System.Collections.Generic.IReadOnlyList<KeyboardLayout> KeyboardLayoutOptions => KeyboardLayouts.All;

    [ObservableProperty]
    private KeyboardLayout _keyboardLayout;

    /// <summary>
    /// A stick that carries a config boots straight into the browser. Without
    /// this the installer never appears, so there would be no way to put
    /// kiosk-os on the internal disk from a wizard-made stick.
    /// </summary>
    [ObservableProperty]
    private bool _installToDisk;

    public ConfigStepViewModel(KioskConfig config) : base(config)
    {
        Title = "Configure the kiosk";
        _homepage = config.Homepage;
        _useWifi = config.Connection == ConnectionType.Wifi;
        _wifiSsid = config.WifiSsid ?? "";
        _wifiPassword = config.WifiPassword ?? "";
        _whitelist = string.Join("|", config.Whitelist);
        _browserModeKiosk = config.BrowserMode == BrowserMode.Kiosk;
        _timezone = config.Timezone;
        _keyboardLayout = KeyboardLayouts.Find(config.KeyboardLayout);
        _installToDisk = config.AutoInstall;
    }

    /// <summary>First problem with the current input, or null.</summary>
    public string? ValidationMessage => BuildConfig(new KioskConfig()).Validate().FirstOrDefault();

    public override bool CanProceed => ValidationMessage is null;

    public override void OnLeaving() => BuildConfig(Config);

    private KioskConfig BuildConfig(KioskConfig target)
    {
        target.Homepage = (Homepage ?? "").Trim();
        target.Connection = UseWifi ? ConnectionType.Wifi : ConnectionType.Wired;
        target.WifiSsid = UseWifi ? WifiSsid.Trim() : null;
        target.WifiPassword = UseWifi ? WifiPassword : null;
        target.BrowserMode = BrowserModeKiosk ? BrowserMode.Kiosk : BrowserMode.Fullscreen;
        target.Timezone = string.IsNullOrWhiteSpace(Timezone) ? "Europe/Berlin" : Timezone.Trim();
        target.KeyboardLayout = (KeyboardLayout ?? KeyboardLayouts.All[0]).Code;
        target.AutoInstall = InstallToDisk;
        target.Whitelist.Clear();
        if (!string.IsNullOrWhiteSpace(Whitelist))
        {
            foreach (var d in Whitelist.Split('|', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
                target.Whitelist.Add(d);
        }
        return target;
    }

    private void Revalidate()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanProceed));
    }

    partial void OnHomepageChanged(string value) => Revalidate();
    partial void OnUseWifiChanged(bool value) => Revalidate();
    partial void OnWifiSsidChanged(string value) => Revalidate();
    partial void OnWifiPasswordChanged(string value) => Revalidate();
}
