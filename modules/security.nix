{ config, lib, pkgs, ... }:

{
  # Disable virtual consoles — no Ctrl+Alt+F1 escape
  services.logind.extraConfig = ''
    NAutoVTs=0
    ReserveVT=0
  '';

  # Block Ctrl+Alt+Del
  systemd.targets."ctrl-alt-del" = {
    enable = false;
  };

  # USB storage blocking (loaded at runtime based on config)
  # When removable_devices=no, this udev rule blocks USB mass storage
  services.udev.extraRules = ''
    # Block USB mass storage devices (enabled by config-fetcher when removable_devices=no)
    # This rule file is managed by kiosk-config.sh
    # SUBSYSTEM=="block", ATTRS{removable}=="1", ENV{UDISKS_IGNORE}="1"
  '';

  # Chromium policies for browser lockdown
  environment.etc."chromium/policies/managed/kiosk-policy.json".text = builtins.toJSON {
    # Disable developer tools
    DeveloperToolsAvailability = 2;
    # Disable task manager
    TaskManagerEndProcessEnabled = false;
    # Disable printing
    PrintingEnabled = false;
    # Disable downloads
    DownloadRestrictions = 3;
    # Disable password manager
    PasswordManagerEnabled = false;
    # Disable autofill
    AutofillAddressEnabled = false;
    AutofillCreditCardEnabled = false;
    # Disable bookmarks
    BookmarkBarEnabled = false;
    EditBookmarksEnabled = false;
    # Disable extensions
    ExtensionInstallBlocklist = [ "*" ];
    # Disable incognito mode toggle (we force it via flags)
    IncognitoModeAvailability = 1;
    # URL whitelist/blacklist (configured at runtime by config-fetcher)
    # URLAllowlist and URLBlocklist are set dynamically
  };

  # Read-only root filesystem considerations
  # NixOS is already immutable by nature — /nix/store is read-only

  # Disable core dumps
  systemd.coredump.enable = false;

  # Security hardening
  security = {
    # No sudo for kiosk user
    sudo.enable = false;
    # Restrict kernel logs
    protectKernelImage = true;
  };
}
