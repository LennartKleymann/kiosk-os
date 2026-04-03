{ config, lib, pkgs, ... }:

let
  configFetcherScript = pkgs.writeShellScript "kiosk-config-fetcher" ''
    set -euo pipefail

    CONFIG_DIR="/etc/kiosk"
    CONFIG_FILE="$CONFIG_DIR/config"
    USB_CONFIG_FILE=""

    # 1. Check for config on USB boot partition
    # The wizard writes config to a small partition labeled KIOSK_CFG
    if [ -b /dev/disk/by-label/KIOSK_CFG ]; then
      mkdir -p /mnt/kiosk-cfg
      mount -o ro /dev/disk/by-label/KIOSK_CFG /mnt/kiosk-cfg 2>/dev/null || true
      if [ -f /mnt/kiosk-cfg/kiosk.conf ]; then
        USB_CONFIG_FILE="/mnt/kiosk-cfg/kiosk.conf"
        echo "[kiosk-config] Found config on USB partition"
      fi
    fi

    # 2. Use USB config as base, or fall back to default
    if [ -n "$USB_CONFIG_FILE" ]; then
      cp "$USB_CONFIG_FILE" "$CONFIG_FILE"
      echo "[kiosk-config] Loaded config from USB"
    elif [ ! -f "$CONFIG_FILE" ] && [ -f /etc/kiosk/default.conf ]; then
      cp /etc/kiosk/default.conf "$CONFIG_FILE"
      echo "[kiosk-config] No USB/remote config found, using default"
    fi

    # 3. Preserve local-only settings before remote override
    # auto_install must NEVER come from a remote config (security risk)
    LOCAL_AUTO_INSTALL=""
    if [ -f "$CONFIG_FILE" ]; then
      LOCAL_AUTO_INSTALL=$(grep -E "^auto_install=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs || echo "")
    fi

    # 4. If config has a kiosk_config URL, fetch remote config (overrides USB)
    if [ -f "$CONFIG_FILE" ]; then
      REMOTE_URL=$(grep -E "^kiosk_config=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs)
      if [ -n "$REMOTE_URL" ]; then
        echo "[kiosk-config] Fetching remote config from $REMOTE_URL"
        if ${pkgs.curl}/bin/curl -sfL "$REMOTE_URL" -o "$CONFIG_FILE.remote" --connect-timeout 10; then
          # Remove auto_install from remote config if present (security)
          grep -v "^auto_install=" "$CONFIG_FILE.remote" > "$CONFIG_FILE"
          echo "[kiosk-config] Remote config loaded successfully"
        else
          echo "[kiosk-config] Remote config fetch failed, using local config"
        fi
        rm -f "$CONFIG_FILE.remote"
      fi
    fi

    # 5. Restore local-only auto_install setting
    if [ -n "$LOCAL_AUTO_INSTALL" ]; then
      echo "auto_install=$LOCAL_AUTO_INSTALL" >> "$CONFIG_FILE"
    fi

    # 4. Apply network config (WiFi)
    if [ -f "$CONFIG_FILE" ]; then
      CONNECTION=$(grep -E "^connection=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs)
      WIFI_SSID=$(grep -E "^wifi_ssid=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs)
      WIFI_PASS=$(grep -E "^wifi_password=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs)

      if [ "$CONNECTION" = "wifi" ] && [ -n "$WIFI_SSID" ]; then
        echo "[kiosk-config] Configuring WiFi: $WIFI_SSID"
        ${pkgs.wpa_supplicant}/bin/wpa_passphrase "$WIFI_SSID" "$WIFI_PASS" > /etc/wpa_supplicant.conf
        # wpa_supplicant will pick this up
      fi
    fi

    # 5. Apply timezone
    if [ -f "$CONFIG_FILE" ]; then
      TZ=$(grep -E "^timezone=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs)
      if [ -n "$TZ" ]; then
        ln -sf "/usr/share/zoneinfo/$TZ" /etc/localtime 2>/dev/null || true
      fi
    fi

    # 6. Apply USB blocking
    if [ -f "$CONFIG_FILE" ]; then
      REMOVABLE=$(grep -E "^removable_devices=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs)
      if [ "$REMOVABLE" = "no" ]; then
        echo 'SUBSYSTEM=="block", ATTRS{removable}=="1", ENV{UDISKS_IGNORE}="1"' > /etc/udev/rules.d/99-kiosk-block-usb.rules
        udevadm control --reload-rules 2>/dev/null || true
        echo "[kiosk-config] USB mass storage blocked"
      fi
    fi

    # 7. Apply Chromium whitelist
    if [ -f "$CONFIG_FILE" ]; then
      WHITELIST=$(grep -E "^whitelist=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs)
      if [ -n "$WHITELIST" ]; then
        # Convert pipe-separated list to JSON array
        ALLOW_LIST=$(echo "$WHITELIST" | tr '|' '\n' | sed 's/.*/"&"/' | tr '\n' ',' | sed 's/,$//')
        cat > /etc/chromium/policies/managed/kiosk-whitelist.json <<POLICY
    {
      "URLBlocklist": ["*"],
      "URLAllowlist": [$ALLOW_LIST]
    }
    POLICY
        echo "[kiosk-config] Whitelist applied: $WHITELIST"
      fi
    fi

    echo "[kiosk-config] Configuration complete"

    # Unmount USB config partition
    umount /mnt/kiosk-cfg 2>/dev/null || true
  '';
in
{
  # Config fetcher runs early in boot, before the kiosk session
  systemd.services.kiosk-config-fetcher = {
    description = "Fetch and apply kiosk configuration";
    wantedBy = [ "multi-user.target" ];
    before = [ "cage-tty1.service" "kiosk-idle-watcher.service" ];
    after = [ "network-online.target" ];
    wants = [ "network-online.target" ];
    serviceConfig = {
      Type = "oneshot";
      RemainAfterExit = true;
      ExecStart = "${configFetcherScript}";
    };
  };

  # Ship default config with the image
  environment.etc."kiosk/default.conf" = {
    source = ../configs/default.conf;
    mode = "0644";
  };

  # Ensure config directory and chromium policy directory exist
  systemd.tmpfiles.rules = [
    "d /etc/kiosk 0755 root root -"
    "d /etc/chromium/policies/managed 0755 root root -"
  ];
}
