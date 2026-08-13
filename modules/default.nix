{ ... }:

{
  imports = [
    ./boot.nix            # Silent boot with Plymouth splash
    ./kiosk.nix           # Cage compositor + Chromium kiosk session
    ./display.nix         # Wallpaper, fonts
    ./networking.nix      # DHCP, WiFi, firewall
    ./security.nix        # Chromium policies, logind hardening
    ./power.nix           # Idle detection (DPMS, session reset)
    ./config-fetcher.nix  # Load config from USB partition or remote URL
    ./admin.nix           # Optional SSH (disabled by default)
    ./installer.nix       # Browser-based disk installer
  ];
}
