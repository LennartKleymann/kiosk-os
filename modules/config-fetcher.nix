{ pkgs, ... }:

let
  # Reads one key from the active kiosk config: kiosk-conf <key> [default]
  # Shared by every runtime script so they all parse the file the same way.
  kioskConf = pkgs.writeShellScriptBin "kiosk-conf" ''
    CONFIG_FILE="''${KIOSK_CONFIG_FILE:-/etc/kiosk/config}"
    value=""
    if [ -f "$CONFIG_FILE" ]; then
      value=$(${pkgs.gnugrep}/bin/grep -E "^[[:space:]]*$1[[:space:]]*=" "$CONFIG_FILE" 2>/dev/null \
        | ${pkgs.coreutils}/bin/tail -n1 | ${pkgs.coreutils}/bin/cut -d'=' -f2- \
        | ${pkgs.gnused}/bin/sed 's/^[[:space:]]*//; s/[[:space:]]*$//')
    fi
    if [ -z "$value" ]; then printf '%s\n' "''${2-}"; else printf '%s\n' "$value"; fi
  '';

  configFetcherScript = pkgs.writeShellScript "kiosk-config-fetcher" ''
    set -uo pipefail
    export PATH="/run/current-system/sw/bin:$PATH"

    CONFIG_DIR="/etc/kiosk"
    CONFIG_FILE="$CONFIG_DIR/config"
    RUN_DIR="/run/kiosk"
    install -d -m 0755 "$CONFIG_DIR" "$RUN_DIR"

    log() { echo "[kiosk-config] $*"; }
    config_value() { kiosk-conf "$@"; }

    # People edit kiosk.conf with Notepad: strip a UTF-8 BOM and CRLF line
    # endings, otherwise every value silently carries a trailing \r.
    normalize() { sed -e '1s/^\xEF\xBB\xBF//' -e 's/\r$//' "$1"; }

    # Several partitions can carry the KIOSK_CFG label at once — the USB stick
    # and an installed disk, while the stick is still plugged in. Prefer the
    # one on the disk we booted from: the stick when live, the disk otherwise.
    disk_of() { lsblk -no PKNAME "$1" 2>/dev/null | head -n1; }
    BOOT_SRC=$(findmnt -no SOURCE /iso 2>/dev/null || findmnt -no SOURCE / 2>/dev/null || true)
    BOOT_DISK=""
    case "$BOOT_SRC" in /dev/*) BOOT_DISK=$(disk_of "$BOOT_SRC"); [ -n "$BOOT_DISK" ] || BOOT_DISK=''${BOOT_SRC#/dev/} ;; esac

    CFG_DEV=""
    for dev in $(blkid -t LABEL=KIOSK_CFG -o device 2>/dev/null); do
      [ -z "$CFG_DEV" ] && CFG_DEV="$dev"
      if [ -n "$BOOT_DISK" ] && [ "$(disk_of "$dev")" = "$BOOT_DISK" ]; then
        CFG_DEV="$dev"; break
      fi
    done
    # for the diagnostics report (modules/diagnostics.nix)
    printf '%s' "$CFG_DEV" > "$RUN_DIR/cfg-device"
    printf '%s' "$BOOT_SRC" > "$RUN_DIR/boot-device"

    USB_CONFIG_FILE=""
    if [ -n "$CFG_DEV" ]; then
      mkdir -p /mnt/kiosk-cfg
      if mount -o ro "$CFG_DEV" /mnt/kiosk-cfg; then
        if [ -f /mnt/kiosk-cfg/kiosk.conf ]; then
          USB_CONFIG_FILE="/mnt/kiosk-cfg/kiosk.conf"
          log "Found config on $CFG_DEV"
        fi
      else
        log "Failed to mount KIOSK_CFG partition $CFG_DEV"
      fi
    fi

    CONFIG_SOURCE="default"
    if [ -n "$USB_CONFIG_FILE" ]; then
      normalize "$USB_CONFIG_FILE" > "$CONFIG_FILE"
      CONFIG_SOURCE="usb"
      log "Loaded config from KIOSK_CFG"
    elif [ -f "$CONFIG_DIR/default.conf" ]; then
      normalize "$CONFIG_DIR/default.conf" > "$CONFIG_FILE"
      log "Using default config"
    fi
    umount /mnt/kiosk-cfg 2>/dev/null || true
    chmod 0644 "$CONFIG_FILE"
    printf '%s' "$CONFIG_SOURCE" > /run/kiosk-config-source
    # Pristine local config: the disk installer copies this one, not the
    # merged result, so a remote config URL keeps working after installing.
    cp "$CONFIG_FILE" "$RUN_DIR/local.conf"

    # --- Network ---
    # Applied from the local config, before the remote config is fetched:
    # a WiFi kiosk needs its WiFi to reach the remote config at all, and a
    # remote file can never cut a kiosk off its own network.
    # --- WiFi ---
    CONNECTION=$(config_value connection wired)
    WIFI_SSID=$(config_value wifi_ssid "")
    WIFI_PASS=$(config_value wifi_password "")

    if [ "$CONNECTION" = "wifi" ] && [ -n "$WIFI_SSID" ]; then
      log "Configuring WiFi: $WIFI_SSID"
      umask 077
      written=no
      if [ -z "$WIFI_PASS" ]; then
        # Open network
        printf 'network={\n\tssid="%s"\n\tscan_ssid=1\n\tkey_mgmt=NONE\n}\n' "$WIFI_SSID" > /etc/wpa_supplicant.conf
        written=yes
      elif wpa_passphrase "$WIFI_SSID" "$WIFI_PASS" > "$RUN_DIR/wpa.tmp" 2>/dev/null; then
        # scan_ssid=1 also finds networks that hide their SSID
        grep -v '^[[:space:]]*#psk=' "$RUN_DIR/wpa.tmp" | sed 's/^}$/\tscan_ssid=1\n}/' > /etc/wpa_supplicant.conf
        written=yes
      else
        log "Invalid WiFi password (WPA needs 8 to 63 characters) — WiFi not configured"
      fi
      rm -f "$RUN_DIR/wpa.tmp"
      umask 022
      if [ "$written" = yes ]; then
        systemctl restart wpa_supplicant.service 2>/dev/null \
          || log "Could not restart wpa_supplicant"
      fi
    fi

    # --- Static IP (dhcp=no) ---
    if [ "$(config_value dhcp yes)" = "no" ]; then
      IP=$(config_value ip "")
      NETMASK=$(config_value netmask 255.255.255.0)
      GATEWAY=$(config_value gateway "")
      DNS=$(config_value dns "")
      if [ -n "$IP" ]; then
        PREFIX=0
        for octet in $(echo "$NETMASK" | tr '.' ' '); do
          while [ "$octet" -gt 0 ]; do PREFIX=$((PREFIX + (octet & 1))); octet=$((octet >> 1)); done
        done
        IFACE=""
        for n in $(ls /sys/class/net); do
          [ "$n" = lo ] && continue
          if [ "$CONNECTION" = "wifi" ]; then
            [ -d "/sys/class/net/$n/wireless" ] && { IFACE=$n; break; }
          else
            [ -d "/sys/class/net/$n/wireless" ] || { IFACE=$n; break; }
          fi
        done
        if [ -n "$IFACE" ]; then
          systemctl stop dhcpcd.service 2>/dev/null || true
          ip link set "$IFACE" up
          ip addr flush dev "$IFACE"
          ip addr add "$IP/$PREFIX" dev "$IFACE"
          [ -n "$GATEWAY" ] && ip route replace default via "$GATEWAY" dev "$IFACE"
          if [ -n "$DNS" ]; then
            for d in $(echo "$DNS" | tr ',|' '  '); do echo "nameserver $d"; done | resolvconf -a "$IFACE.static"
          fi
          log "Static IP $IP/$PREFIX on $IFACE (gateway $GATEWAY, dns $DNS)"
        else
          log "No network interface found for static IP"
        fi
      fi
    fi

    LOCAL_AUTO_INSTALL=$(config_value auto_install "")

    REMOTE_URL=$(config_value kiosk_config "")
    case "$REMOTE_URL" in
      "") ;;
      https://*) ;;
      *)
        # The remote config decides what the screen shows. Over plain HTTP
        # anyone on the network could point a kiosk at a page of their
        # choosing, so an unencrypted URL is refused rather than trusted.
        log "Refusing non-HTTPS remote config: $REMOTE_URL"
        REMOTE_URL=""
        ;;
    esac

    if [ -n "$REMOTE_URL" ]; then
      log "Fetching remote config from $REMOTE_URL"
      # The network may still be coming up: retry for about 30 seconds.
      ok=no
      for _ in $(seq 1 10); do
        if curl -sfL --proto '=https' --proto-redir '=https' --connect-timeout 5 --max-time 20 \
            "$REMOTE_URL" -o "$RUN_DIR/remote.conf"; then
          ok=yes; break
        fi
        sleep 3
      done
      if [ "$ok" = yes ]; then
        # Remote values override local ones key by key (the last occurrence
        # wins); keys the remote file does not set keep their local value.
        {
          cat "$RUN_DIR/local.conf"
          echo "# --- remote config: $REMOTE_URL"
          normalize "$RUN_DIR/remote.conf" | grep -v "^[[:space:]]*auto_install[[:space:]]*="
          [ -n "$LOCAL_AUTO_INSTALL" ] && echo "auto_install=$LOCAL_AUTO_INSTALL"
        } > "$CONFIG_FILE"
        log "Remote config loaded"
      else
        log "Remote config fetch failed, using local"
      fi
      rm -f "$RUN_DIR/remote.conf"
    fi

    # --- Timezone ---
    TZ_VALUE=$(config_value timezone Europe/Berlin)
    if [ -f "/etc/zoneinfo/$TZ_VALUE" ]; then
      timedatectl set-timezone "$TZ_VALUE" 2>/dev/null || ln -sfn "/etc/zoneinfo/$TZ_VALUE" /etc/localtime
      log "Timezone set to $TZ_VALUE"
    else
      log "Invalid or unsupported timezone: $TZ_VALUE"
    fi

    # --- Keyboard layout (read by cage via EnvironmentFile) ---
    printf 'XKB_DEFAULT_LAYOUT=%s\n' "$(config_value primary_keyboard_layout us)" > "$RUN_DIR/cage.env"

    # --- USB mass storage ---
    # /etc/udev/rules.d is read-only on NixOS; /run/udev/rules.d is not.
    # De-authorizing the USB interface blocks storage plugged in after boot,
    # while the stick the kiosk booted from keeps working.
    rm -f /run/udev/rules.d/99-kiosk-block-usb.rules
    if [ "$(config_value removable_devices yes)" = "no" ]; then
      mkdir -p /run/udev/rules.d
      echo 'ACTION=="add", SUBSYSTEM=="usb", ENV{DEVTYPE}=="usb_interface", ATTR{bInterfaceClass}=="08", ATTR{authorized}="0"' \
        > /run/udev/rules.d/99-kiosk-block-usb.rules
      udevadm control --reload-rules 2>/dev/null || true
      log "USB mass storage blocked"
    fi

    # --- Domain whitelist ---
    mkdir -p /etc/chromium/policies/managed
    rm -f /etc/chromium/policies/managed/kiosk-whitelist.json
    WHITELIST=$(config_value whitelist "")
    if [ -n "$WHITELIST" ]; then
      ALLOW_LIST=$(printf '%s' "$WHITELIST" | tr '|' '\n' | sed 's/^[[:space:]]*//; s/[[:space:]]*$//' \
        | grep -v '^$' | sed 's/.*/"&"/' | tr '\n' ',' | sed 's/,$//')
      # The local error page and the installer must stay reachable.
      cat > /etc/chromium/policies/managed/kiosk-whitelist.json <<POLICY
    {
      "URLBlocklist": ["*"],
      "URLAllowlist": [$ALLOW_LIST, "file:///etc/kiosk/", "http://127.0.0.1:8484"]
    }
    POLICY
      log "Whitelist applied: $WHITELIST"
    fi

    # --- SSH admin access ---
    if [ "$(config_value admin_ssh no)" = "yes" ]; then
      KEY=$(config_value admin_ssh_key "")
      if [ -n "$KEY" ]; then
        install -d -m 0700 -o admin -g users /home/admin/.ssh
        printf '%s\n' "$KEY" > /home/admin/.ssh/authorized_keys
        chown admin:users /home/admin/.ssh/authorized_keys
        chmod 0600 /home/admin/.ssh/authorized_keys
        systemctl start --no-block sshd.service
        log "SSH admin access enabled"
      else
        log "admin_ssh=yes but admin_ssh_key is empty — SSH stays off"
      fi
    fi

    log "Configuration complete"
  '';
in
{
  systemd.services.kiosk-config-fetcher = {
    description = "Fetch and apply kiosk configuration";
    wantedBy = [ "multi-user.target" ];
    before = [ "cage-tty1.service" "kiosk-idle-watcher.service" "kiosk-installer-gate.service" ];
    after = [ "network-online.target" ];
    wants = [ "network-online.target" ];
    serviceConfig = {
      Type = "oneshot";
      RemainAfterExit = true;
      ExecStart = "${configFetcherScript}";
    };
  };

  environment.systemPackages = [ kioskConf ];

  # Ship default config with the image
  environment.etc."kiosk/default.conf" = {
    source = ../configs/default.conf;
    mode = "0644";
  };

  systemd.tmpfiles.rules = [
    "d /etc/kiosk 0755 root root -"
    "d /run/kiosk 0755 root root -"
    "d /etc/chromium/policies/managed 0755 root root -"
  ];
}
