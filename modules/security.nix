{ config, lib, pkgs, ... }:

{
  # Disable virtual consoles and Ctrl+Alt+Del
  services.logind.extraConfig = ''
    NAutoVTs=0
    ReserveVT=0
    HandleRebootKey=ignore
    HandleRebootKeySec=0
  '';

  # Chromium enterprise policies for browser lockdown
  environment.etc."chromium/policies/managed/kiosk-policy.json".text = builtins.toJSON {
    DeveloperToolsAvailability = 2;
    TaskManagerEndProcessEnabled = false;
    PrintingEnabled = false;
    DownloadRestrictions = 3;
    PasswordManagerEnabled = false;
    AutofillAddressEnabled = false;
    AutofillCreditCardEnabled = false;
    BookmarkBarEnabled = false;
    EditBookmarksEnabled = false;
    ExtensionInstallBlocklist = [ "*" ];
    IncognitoModeAvailability = 1;
    BrowserSignin = 0;
    SyncDisabled = true;
    ShowHomeButton = true;
    HomepageIsNewTabPage = true;
    MaximumTabsPerBrowser = 1;
  };

  # Disable core dumps
  systemd.coredump.enable = false;

  # No sudo for kiosk user
  security.sudo.enable = false;
}
