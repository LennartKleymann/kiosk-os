{ pkgs, ... }:

let
  configFetcherScript = pkgs.writeShellScript "kiosk-config-fetcher" ''
    set -uo pipefail
    export PATH="/run/current-system/sw/bin:$PATH"

    CONFIG_DIR="/etc/kiosk"
    CONFIG_FILE="$CONFIG_DIR/config"
    USB_CONFIG_FILE=""

    config_value() {
      local key="$1"
      local fallback="''${2-}"
      local value=""
      if [ -f "$CONFIG_FILE" ]; then
        value=$(grep -E "^[[:space:]]*$key[[:space:]]*=" "$CONFIG_FILE" 2>/dev/null \
          | tail -n1 | cut -d'=' -f2- | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')
      fi
      if [ -z "$value" ]; then
        printf '%s' "$fallback"
      else
        printf '%s' "$value"
      fi
    }

    if [ -b /dev/disk/by-label/KIOSK_CFG ]; then
      mkdir -p /mnt/kiosk-cfg
      if mount -o ro /dev/disk/by-label/KIOSK_CFG /mnt/kiosk-cfg; then
        if [ -f /mnt/kiosk-cfg/kiosk.conf ]; then
          USB_CONFIG_FILE="/mnt/kiosk-cfg/kiosk.conf"
          echo "[kiosk-config] Found config on USB partition"
        fi
      else
        echo "[kiosk-config] Failed to mount KIOSK_CFG partition"
      fi
    fi

    mkdir -p "$CONFIG_DIR"

    CONFIG_SOURCE="default"
    if [ -n "$USB_CONFIG_FILE" ]; then
      cp "$USB_CONFIG_FILE" "$CONFIG_FILE"
      CONFIG_SOURCE="usb"
      echo "[kiosk-config] Loaded config from USB"
    elif [ -f "$CONFIG_FILE" ]; then
      CONFIG_SOURCE="existing"
    elif [ -f "$CONFIG_DIR/default.conf" ]; then
      cp "$CONFIG_DIR/default.conf" "$CONFIG_FILE"
      echo "[kiosk-config] Using default config"
    fi
    printf '%s' "$CONFIG_SOURCE" > /run/kiosk-config-source

    LOCAL_AUTO_INSTALL=$(config_value auto_install "")

    REMOTE_URL=$(config_value kiosk_config "")
    if [ -n "$REMOTE_URL" ]; then
      echo "[kiosk-config] Fetching remote config from $REMOTE_URL"
      if ${pkgs.curl}/bin/curl -sfL "$REMOTE_URL" -o "$CONFIG_FILE.remote" --connect-timeout 10; then
        grep -v "^[[:space:]]*auto_install[[:space:]]*=" "$CONFIG_FILE.remote" > "$CONFIG_FILE.clean" || true
        mv "$CONFIG_FILE.clean" "$CONFIG_FILE"
        echo "[kiosk-config] Remote config loaded"
      else
        echo "[kiosk-config] Remote config fetch failed, using local"
      fi
      rm -f "$CONFIG_FILE.remote" "$CONFIG_FILE.clean"
    fi

    if [ -n "$LOCAL_AUTO_INSTALL" ]; then
      echo "auto_install=$LOCAL_AUTO_INSTALL" >> "$CONFIG_FILE"
    fi

    CONNECTION=$(config_value connection wired)
    WIFI_SSID=$(config_value wifi_ssid "")
    WIFI_PASS=$(config_value wifi_password "")

    if [ "$CONNECTION" = "wifi" ] && [ -n "$WIFI_SSID" ]; then
      echo "[kiosk-config] Configuring WiFi: $WIFI_SSID"
      umask 077
      if ${pkgs.wpa_supplicant}/bin/wpa_passphrase "$WIFI_SSID" "$WIFI_PASS" \
          | grep -v '^[[:space:]]*#psk=' > /etc/wpa_supplicant.conf; then
        systemctl restart wpa_supplicant.service 2>/dev/null \
          || systemctl restart "wpa_supplicant@*.service" 2>/dev/null \
          || echo "[kiosk-config] Could not restart wpa_supplicant"
      else
        echo "[kiosk-config] Failed to write WiFi configuration"
      fi
      umask 022
    fi

    TZ_VALUE=$(config_value timezone "")
    if [ -n "$TZ_VALUE" ]; then
      if timedatectl set-timezone "$TZ_VALUE" 2>/dev/null; then
        echo "[kiosk-config] Timezone set to $TZ_VALUE"
      else
        echo "[kiosk-config] Invalid or unsupported timezone: $TZ_VALUE"
      fi
    fi

    REMOVABLE=$(config_value removable_devices yes)
    if [ "$REMOVABLE" = "no" ]; then
      echo 'SUBSYSTEM=="block", ATTRS{removable}=="1", ENV{UDISKS_IGNORE}="1"' \
        > /etc/udev/rules.d/99-kiosk-block-usb.rules
      udevadm control --reload-rules 2>/dev/null || true
      echo "[kiosk-config] USB mass storage blocked"
    fi

    WHITELIST=$(config_value whitelist "")
    if [ -n "$WHITELIST" ]; then
      mkdir -p /etc/chromium/policies/managed
      ALLOW_LIST=$(printf '%s' "$WHITELIST" | tr '|' '\n' | sed 's/.*/"&"/' | tr '\n' ',' | sed 's/,$//')
      cat > /etc/chromium/policies/managed/kiosk-whitelist.json <<POLICY
    {
      "URLBlocklist": ["*"],
      "URLAllowlist": [$ALLOW_LIST]
    }
    POLICY
      echo "[kiosk-config] Whitelist applied: $WHITELIST"
    fi

    echo "[kiosk-config] Configuration complete"
    umount /mnt/kiosk-cfg 2>/dev/null || true
  '';
in
{
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

  systemd.tmpfiles.rules = [
    "d /etc/kiosk 0755 root root -"
    "d /etc/chromium/policies/managed 0755 root root -"
  ];
}
