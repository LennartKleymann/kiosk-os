{ config, lib, pkgs, ... }:

let
  kioskStartScript = pkgs.writeShellScript "kiosk-start" ''
    set -uo pipefail
    export PATH="/run/current-system/sw/bin:$PATH"

    HOMEPAGE=$(kiosk-conf homepage https://example.com)
    WALLPAPER=$(kiosk-conf wallpaper)
    BROWSER_MODE=$(kiosk-conf browser_mode kiosk)

    # Installer override (set by kiosk-installer-gate on live media)
    OVERRIDE=""
    if [ -f /run/kiosk/homepage-override ]; then
      OVERRIDE=$(cat /run/kiosk/homepage-override)
    fi

    # Wallpaper (visible during startup and if the browser restarts)
    if [ -n "$WALLPAPER" ]; then
      curl -sfL --max-time 15 "$WALLPAPER" -o "$XDG_RUNTIME_DIR/wallpaper" 2>/dev/null || true
      if [ -s "$XDG_RUNTIME_DIR/wallpaper" ]; then
        ${pkgs.swaybg}/bin/swaybg -i "$XDG_RUNTIME_DIR/wallpaper" -m fill &
      fi
    elif [ -f /etc/kiosk/wallpaper-default.jpg ]; then
      ${pkgs.swaybg}/bin/swaybg -i /etc/kiosk/wallpaper-default.jpg -m fill &
    fi

    URL="$HOMEPAGE"
    if [ -n "$OVERRIDE" ]; then
      URL="$OVERRIDE"
    else
      # Wait up to 30s for the homepage to become reachable. If it is not,
      # show the local error page, which keeps retrying and redirects once
      # the homepage is up.
      case "$HOMEPAGE" in
        http://*|https://*)
          reachable=no
          for i in $(seq 1 15); do
            if curl -s -o /dev/null --connect-timeout 2 --max-time 5 "$HOMEPAGE"; then
              reachable=yes; break
            fi
            sleep 2
          done
          if [ "$reachable" = no ]; then
            ENC=$(printf '%s' "$HOMEPAGE" | ${pkgs.jq}/bin/jq -sRr @uri)
            URL="file:///etc/kiosk/error-page.html?url=$ENC"
          fi
          ;;
      esac
    fi

    # kiosk: no browser UI at all. fullscreen: toolbar with back button and
    # address bar — cage already gives the window the whole screen, while
    # Chromium's own --start-fullscreen would hide exactly that toolbar.
    MODE_FLAGS="--kiosk"
    [ "$BROWSER_MODE" = "fullscreen" ] && MODE_FLAGS="--start-maximized"

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
      --disable-pinch \
      --overscroll-history-navigation=0 \
      --check-for-update-interval=31536000 \
      --disable-gpu \
      --ozone-platform=wayland \
      --user-data-dir="$XDG_RUNTIME_DIR/chromium" \
      "$URL"
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

  systemd.services."cage-tty1" = {
    after = [ "kiosk-config-fetcher.service" "kiosk-installer-gate.service" ];
    wants = [ "kiosk-config-fetcher.service" ];
    # Keyboard layout etc. written by the config fetcher
    serviceConfig.EnvironmentFile = "-/run/kiosk/cage.env";
    serviceConfig.Restart = lib.mkForce "always";
    serviceConfig.RestartSec = 2;
  };

  environment.etc."kiosk/error-page.html" = {
    source = ../assets/error-page.html;
    mode = "0644";
  };

  environment.systemPackages = with pkgs; [
    chromium
    cage
    swaybg
    curl
    jq
  ];

  environment.sessionVariables = {
    NIXOS_OZONE_WL = "1";
  };

  hardware.graphics.enable = true;
}
