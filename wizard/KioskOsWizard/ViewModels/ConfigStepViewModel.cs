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

    public ConfigStepViewModel(KioskConfig config) : base(config)
    {
        Title = "Configure the kiosk";
        _homepage = config.Homepage;
        _useWifi = config.Connection == ConnectionType.Wifi;
        _wifiSsid = config.WifiSsid ?? "";
        _wifiPassword = config.WifiPassword ?? "";
        _whitelist = string.Join("|", config.Whitelist);
        _browserModeKiosk = config.BrowserMode == BrowserMode.Kiosk;
    }

    public override bool CanProceed =>
        !string.IsNullOrWhiteSpace(Homepage) &&
        (Homepage.StartsWith("http://") || Homepage.StartsWith("https://")) &&
        (!UseWifi || !string.IsNullOrWhiteSpace(WifiSsid));

    public override void OnLeaving()
    {
        Config.Homepage = Homepage.Trim();
        Config.Connection = UseWifi ? ConnectionType.Wifi : ConnectionType.Wired;
        Config.WifiSsid = UseWifi ? WifiSsid.Trim() : null;
        Config.WifiPassword = UseWifi ? WifiPassword : null;
        Config.BrowserMode = BrowserModeKiosk ? BrowserMode.Kiosk : BrowserMode.Fullscreen;
        Config.Whitelist.Clear();
        if (!string.IsNullOrWhiteSpace(Whitelist))
        {
            foreach (var d in Whitelist.Split('|', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
                Config.Whitelist.Add(d);
        }
    }

    partial void OnHomepageChanged(string value) => OnPropertyChanged(nameof(CanProceed));
    partial void OnUseWifiChanged(bool value) => OnPropertyChanged(nameof(CanProceed));
    partial void OnWifiSsidChanged(string value) => OnPropertyChanged(nameof(CanProceed));
}
