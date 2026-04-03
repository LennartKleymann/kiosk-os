{ config, lib, pkgs, ... }:

let
  kioskStartScript = pkgs.writeShellScript "kiosk-start" ''
    set -euo pipefail

    CONFIG_FILE="/etc/kiosk/config"
    HOMEPAGE="https://example.com"
    WALLPAPER=""

    # Load config if present
    if [ -f "$CONFIG_FILE" ]; then
      while IFS='=' read -r key value; do
        # Skip comments and empty lines
        [[ "$key" =~ ^#.*$ || -z "$key" ]] && continue
        # Trim whitespace
        key=$(echo "$key" | xargs)
        value=$(echo "$value" | xargs)
        case "$key" in
          homepage) HOMEPAGE="$value" ;;
          wallpaper) WALLPAPER="$value" ;;
        esac
      done < "$CONFIG_FILE"
    fi

    # Installer override: if the installer gate wrote a homepage override, use it
    if [ -f /tmp/kiosk-homepage-override ]; then
      HOMEPAGE=$(cat /tmp/kiosk-homepage-override)
    fi

    # Download wallpaper if configured
    if [ -n "$WALLPAPER" ]; then
      ${pkgs.curl}/bin/curl -sL "$WALLPAPER" -o /tmp/wallpaper.jpg 2>/dev/null || true
      if [ -f /tmp/wallpaper.jpg ]; then
        ${pkgs.swaybg}/bin/swaybg -i /tmp/wallpaper.jpg -m fill &
      fi
    elif [ -f /etc/kiosk/wallpaper-default.jpg ]; then
      ${pkgs.swaybg}/bin/swaybg -i /etc/kiosk/wallpaper-default.jpg -m fill &
    fi

    # Note: Mouse cursor hiding is handled via seat configuration
    # See modules/display.nix for hide_mouse support

    # Launch Chromium in kiosk mode
    exec ${pkgs.chromium}/bin/chromium \
      --kiosk \
      --no-first-run \
      --noerrdialogs \
      --disable-infobars \
      --disable-session-crashed-bubble \
      --disable-component-update \
      --disable-background-networking \
      --disable-client-side-phishing-detection \
      --disable-extensions \
      --disable-translate \
      --disable-sync \
      --disable-features=TranslateUI \
      --disable-gpu \
      --incognito \
      --ozone-platform=wayland \
      "$HOMEPAGE"
  '';
in
{
  # Create kiosk user
  users.users.kiosk = {
    isNormalUser = true;
    home = "/home/kiosk";
    group = "kiosk";
  };
  users.groups.kiosk = {};

  # Auto-login and start Cage with kiosk script
  services.cage = {
    enable = true;
    user = "kiosk";
    program = "${kioskStartScript}";
    extraArguments = [ "-d" ];
  };

  # Ensure config directory exists
  systemd.tmpfiles.rules = [
    "d /etc/kiosk 0755 root root -"
  ];

  # Required packages available system-wide
  environment.systemPackages = with pkgs; [
    chromium
    cage
    swaybg
    curl
  ];

  # Allow Chromium to run in Wayland
  environment.sessionVariables = {
    NIXOS_OZONE_WL = "1";
  };

  # Hardware acceleration
  hardware.graphics.enable = true;
}
