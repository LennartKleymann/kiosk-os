{ config, lib, pkgs, ... }:

let
  kioskStartScript = pkgs.writeShellScript "kiosk-start" ''
    set -euo pipefail

    CONFIG_FILE="/etc/kiosk/config"
    HOMEPAGE="https://example.com"
    WALLPAPER=""
    BROWSER_MODE="kiosk"

    # Parse config
    if [ -f "$CONFIG_FILE" ]; then
      while IFS='=' read -r key value; do
        [[ "$key" =~ ^#.*$ || -z "$key" ]] && continue
        key=$(echo "$key" | xargs)
        value=$(echo "$value" | xargs)
        case "$key" in
          homepage) HOMEPAGE="$value" ;;
          wallpaper) WALLPAPER="$value" ;;
          browser_mode) BROWSER_MODE="$value" ;;
        esac
      done < "$CONFIG_FILE"
    fi

    # Installer override
    if [ -f /tmp/kiosk-homepage-override ]; then
      HOMEPAGE=$(cat /tmp/kiosk-homepage-override)
    fi

    # Wallpaper
    if [ -n "$WALLPAPER" ]; then
      ${pkgs.curl}/bin/curl -sL "$WALLPAPER" -o /tmp/wallpaper.jpg 2>/dev/null || true
      if [ -f /tmp/wallpaper.jpg ]; then
        ${pkgs.swaybg}/bin/swaybg -i /tmp/wallpaper.jpg -m fill &
      fi
    elif [ -f /etc/kiosk/wallpaper-default.jpg ]; then
      ${pkgs.swaybg}/bin/swaybg -i /etc/kiosk/wallpaper-default.jpg -m fill &
    fi

    # Browser mode flags
    MODE_FLAGS=""
    case "$BROWSER_MODE" in
      fullscreen) MODE_FLAGS="--start-fullscreen" ;;
      *)          MODE_FLAGS="--kiosk" ;;
    esac

    # Launch Chromium
    exec ${pkgs.chromium}/bin/chromium \
      $MODE_FLAGS \
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
  users.users.kiosk = {
    isNormalUser = true;
    home = "/home/kiosk";
    group = "kiosk";
  };
  users.groups.kiosk = {};

  services.cage = {
    enable = true;
    user = "kiosk";
    program = "${kioskStartScript}";
    extraArguments = [ "-d" ];
  };

  systemd.tmpfiles.rules = [
    "d /etc/kiosk 0755 root root -"
  ];

  environment.systemPackages = with pkgs; [
    chromium
    cage
    swaybg
    curl
  ];

  environment.sessionVariables = {
    NIXOS_OZONE_WL = "1";
  };

  hardware.graphics.enable = true;
}
