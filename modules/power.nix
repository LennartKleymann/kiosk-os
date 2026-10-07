{ config, lib, pkgs, ... }:

let
  # cage has no wlr-output-power-management (so wlopm cannot work), but it
  # does implement wlr-output-management: disabling the output puts the
  # monitor into standby just the same.
  displayPower = pkgs.writeShellScript "kiosk-display-power" ''
    for output in $(${pkgs.wlr-randr}/bin/wlr-randr | ${pkgs.gawk}/bin/awk '/^[^ ]/ {print $1}'); do
      ${pkgs.wlr-randr}/bin/wlr-randr --output "$output" --"$1"
    done
  '';

  # Touches a marker file on the first key press, click, touch or mouse
  # movement on any input device (including ones plugged in later).
  inputActivity = pkgs.writers.writePython3 "kiosk-input-activity" { flakeIgnore = [ "E501" ]; } ''
    import glob
    import os
    import select
    import struct
    import sys
    import time

    marker = sys.argv[1]
    EVENT = struct.Struct("llHHi")
    EV_KEY, EV_REL, EV_ABS = 1, 2, 3
    fds = {}

    while True:
        for path in glob.glob("/dev/input/event*"):
            if path not in fds:
                try:
                    fds[path] = os.open(path, os.O_RDONLY | os.O_NONBLOCK)
                except OSError:
                    pass
        ready, _, _ = select.select(list(fds.values()), [], [], 10)
        for fd in ready:
            try:
                data = os.read(fd, EVENT.size * 64)
            except OSError:
                for p, f in list(fds.items()):
                    if f == fd:
                        os.close(f)
                        del fds[p]
                continue
            for i in range(0, len(data) - EVENT.size + 1, EVENT.size):
                _, _, etype, _, _ = EVENT.unpack_from(data, i)
                if etype in (EV_KEY, EV_REL, EV_ABS):
                    with open(marker, "w"):
                        pass
                    sys.exit(0)
        time.sleep(0.1)
  '';

  idleWatcherScript = pkgs.writeShellScript "kiosk-idle-watcher" ''
    set -uo pipefail
    export PATH="/run/current-system/sw/bin:$PATH"

    DPMS_IDLE=$(kiosk-conf dpms_idle 0)
    SESSION_IDLE=$(kiosk-conf session_idle 0)
    case "$DPMS_IDLE" in ""|*[!0-9]*) DPMS_IDLE=0 ;; esac
    case "$SESSION_IDLE" in ""|*[!0-9]*) SESSION_IDLE=0 ;; esac

    if [ "$DPMS_IDLE" -eq 0 ] && [ "$SESSION_IDLE" -eq 0 ]; then
      echo "[kiosk-idle] No idle actions configured"
      exec sleep infinity
    fi

    # Attach to the Wayland socket of the kiosk session (cage runs as user kiosk)
    KIOSK_UID=$(id -u kiosk)
    export XDG_RUNTIME_DIR="/run/user/$KIOSK_UID"
    for i in $(seq 1 60); do
      SOCK=$(ls "$XDG_RUNTIME_DIR" 2>/dev/null | grep -E '^wayland-[0-9]+$' | head -n1)
      [ -n "$SOCK" ] && break
      sleep 1
    done
    if [ -z "$SOCK" ]; then
      echo "[kiosk-idle] No Wayland socket found" >&2
      exit 1
    fi
    export WAYLAND_DISPLAY="$SOCK"

    ARGS=()
    if [ "$DPMS_IDLE" -gt 0 ]; then
      ARGS+=(timeout $((DPMS_IDLE * 60)) "${displayPower} off" resume "${displayPower} on")
    fi
    if [ "$SESSION_IDLE" -gt 0 ]; then
      # Reset only a session somebody actually used. Otherwise an untouched
      # kiosk would reload every N minutes, and the restart would also reset
      # the DPMS timer so the screen never turns off. Wayland "resume" events
      # are no proof of use (the browser toggles idle inhibitors on its own),
      # so real input is read from the kernel's input devices instead.
      USED=/run/kiosk/session-used
      rm -f "$USED"
      ${inputActivity} "$USED" &
      ARGS+=(timeout $((SESSION_IDLE * 60)) "[ -f $USED ] && systemctl restart cage-tty1.service")
    fi

    echo "[kiosk-idle] dpms_idle=$DPMS_IDLE session_idle=$SESSION_IDLE (minutes)"
    exec ${pkgs.swayidle}/bin/swayidle -w "''${ARGS[@]}"
  '';
in
{
  # Runs as root so it can restart the kiosk session; it connects to the
  # kiosk user's Wayland socket. Restarted together with the session.
  systemd.services.kiosk-idle-watcher = {
    description = "Kiosk idle watcher for DPMS and session reset";
    wantedBy = [ "cage-tty1.service" ];
    after = [ "cage-tty1.service" "kiosk-config-fetcher.service" ];
    partOf = [ "cage-tty1.service" ];
    serviceConfig = {
      ExecStart = "${idleWatcherScript}";
      Restart = "always";
      RestartSec = 5;
    };
  };

  environment.systemPackages = with pkgs; [
    swayidle
    wlr-randr
  ];
}
