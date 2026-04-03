{ config, lib, pkgs, ... }:

let
  idleWatcherScript = pkgs.writeShellScript "kiosk-idle-watcher" ''
    set -euo pipefail

    CONFIG_FILE="/etc/kiosk/config"
    DPMS_IDLE=0
    SESSION_IDLE=0

    # Load config
    if [ -f "$CONFIG_FILE" ]; then
      while IFS='=' read -r key value; do
        [[ "$key" =~ ^#.*$ || -z "$key" ]] && continue
        key=$(echo "$key" | xargs)
        value=$(echo "$value" | xargs)
        case "$key" in
          dpms_idle) DPMS_IDLE="$value" ;;
          session_idle) SESSION_IDLE="$value" ;;
        esac
      done < "$CONFIG_FILE"
    fi

    # Build swayidle arguments
    ARGS=""

    if [ "$DPMS_IDLE" -gt 0 ] 2>/dev/null; then
      DPMS_SECS=$((DPMS_IDLE * 60))
      ARGS="$ARGS timeout $DPMS_SECS '${pkgs.sway}/bin/swaymsg \"output * dpms off\"' resume '${pkgs.sway}/bin/swaymsg \"output * dpms on\"'"
    fi

    if [ "$SESSION_IDLE" -gt 0 ] 2>/dev/null; then
      SESSION_SECS=$((SESSION_IDLE * 60))
      ARGS="$ARGS timeout $SESSION_SECS 'systemctl restart cage-tty1.service'"
    fi

    if [ -n "$ARGS" ]; then
      exec ${pkgs.swayidle}/bin/swayidle -w $ARGS
    else
      # No idle config — just sleep forever
      exec sleep infinity
    fi
  '';
in
{
  # Idle watcher service
  systemd.services.kiosk-idle-watcher = {
    description = "Kiosk idle watcher for DPMS and session reset";
    wantedBy = [ "graphical-session.target" ];
    after = [ "cage-tty1.service" ];
    serviceConfig = {
      ExecStart = "${idleWatcherScript}";
      Restart = "always";
      RestartSec = 5;
      User = "kiosk";
      Environment = "WAYLAND_DISPLAY=wayland-1";
    };
  };

  # Ensure swayidle is available
  environment.systemPackages = with pkgs; [
    swayidle
  ];
}
