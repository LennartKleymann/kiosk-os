{ pkgs, ... }:

# Writes kiosk-os-diagnostics.txt onto the KIOSK_CFG partition a minute after
# every boot. A kiosk has no screen to show logs on; this way a user can plug
# the stick (or mount the disk's config partition) into any PC and send the
# file — which config was found, which input devices the kernel saw and with
# which driver, and the relevant log lines. Secrets are redacted.

let
  report = pkgs.writeShellScript "kiosk-diagnostics" ''
    set -uo pipefail
    export PATH="/run/current-system/sw/bin:$PATH"
    RUN=/run/kiosk
    OUT=$RUN/diagnostics.txt

    redact() {
      sed -E 's/^([[:space:]]*(wifi_password|plus_token|admin_ssh_key)[[:space:]]*=).*/\1<redacted>/'
    }
    section() { printf '\n===== %s\n' "$1"; }

    {
      echo "kiosk-os diagnostics — $(date -Is)"
      echo "version: $(cat /etc/kiosk/version 2>/dev/null || echo unknown)  system: $(readlink /run/current-system)"
      echo "kernel: $(uname -r)  uptime: $(cut -d. -f1 /proc/uptime) s"
      if mountpoint -q /iso; then echo "mode: live (USB stick)"; else echo "mode: installed"; fi

      section "Configuration"
      echo "booted from: $(cat $RUN/boot-device 2>/dev/null)"
      echo "KIOSK_CFG used: $(cat $RUN/cfg-device 2>/dev/null || echo none)"
      echo "config source: $(cat /run/kiosk-config-source 2>/dev/null)"
      echo "all KIOSK_CFG partitions: $(blkid -t LABEL=KIOSK_CFG -o device 2>/dev/null | tr '\n' ' ')"
      echo "--- files on the KIOSK_CFG partition (kiosk.conf must be named exactly that):"
      DEV=$(cat $RUN/cfg-device 2>/dev/null)
      mkdir -p /mnt/kiosk-diag
      if [ -b "$DEV" ] && mount -o ro "$DEV" /mnt/kiosk-diag; then
        ls -la /mnt/kiosk-diag
        umount /mnt/kiosk-diag
      else
        echo "(not readable)"
      fi
      echo "--- active config (/etc/kiosk/config):"
      redact < /etc/kiosk/config 2>/dev/null

      section "Disks"
      lsblk -o NAME,SIZE,TYPE,TRAN,RM,FSTYPE,LABEL,PTTYPE 2>&1

      section "Input devices (kernel)"
      # N: name, P: physical path, S: sysfs, H: handlers (kbd = keyboard), B: capabilities
      grep -E '^(N|P|H|B: EV)' /proc/bus/input/devices 2>&1
      section "USB devices"
      for d in /sys/bus/usb/devices/*; do
        [ -f "$d/idVendor" ] || continue
        printf '%s %s:%s %s %s\n' "$(basename "$d")" "$(cat $d/idVendor)" "$(cat $d/idProduct)" \
          "$(cat $d/manufacturer 2>/dev/null)" "$(cat $d/product 2>/dev/null)"
      done
      for i in /sys/bus/usb/devices/*:*; do
        [ -f "$i/bInterfaceClass" ] || continue
        printf '  %s class=%s authorized=%s driver=%s\n' "$(basename "$i")" "$(cat $i/bInterfaceClass)" \
          "$(cat $i/authorized 2>/dev/null)" "$(basename "$(readlink $i/driver 2>/dev/null)" 2>/dev/null)"
      done

      section "Kernel messages: input / USB / HID"
      dmesg 2>/dev/null | grep -iE 'input:|hid|usb .*(new|error|fail)|i8042|keyboard|atkbd' | tail -60

      section "Kiosk services"
      journalctl -b -o cat --no-pager -u kiosk-config-fetcher -u kiosk-installer-gate -u cage-tty1 \
        2>/dev/null | grep -vE 'registration_request|DEPRECATED_ENDPOINT' | redact | tail -80

      section "Network"
      ip -4 -o addr show 2>&1 | awk '{print $2, $4}'
      ip route 2>&1 | head -5
    } > "$OUT" 2>&1

    # Copy it onto the config partition (briefly mounted read-write)
    DEV=$(cat $RUN/cfg-device 2>/dev/null)
    [ -b "$DEV" ] || { echo "no KIOSK_CFG partition, report only in $OUT"; exit 0; }
    mkdir -p /mnt/kiosk-diag
    if mount -o rw "$DEV" /mnt/kiosk-diag; then
      sed 's/$/\r/' "$OUT" > /mnt/kiosk-diag/kiosk-os-diagnostics.txt   # CRLF: opens fine in Notepad
      sync
      umount /mnt/kiosk-diag
      echo "diagnostics written to $DEV"
    else
      echo "could not mount $DEV read-write"
    fi
  '';
in
{
  systemd.services.kiosk-diagnostics = {
    description = "Write a diagnostics report to the KIOSK_CFG partition";
    after = [ "kiosk-config-fetcher.service" "cage-tty1.service" ];
    serviceConfig = {
      Type = "oneshot";
      ExecStart = "${report}";
    };
  };

  environment.etc."kiosk/version".text = "0.1.0";

  # A minute after boot, so the log shows the kiosk session starting
  systemd.timers.kiosk-diagnostics = {
    wantedBy = [ "timers.target" ];
    timerConfig.OnBootSec = "60s";
  };
}
