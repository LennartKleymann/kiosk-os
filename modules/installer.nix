{ pkgs, ... }:

let
  installerApi = pkgs.writeScript "kiosk-installer-api" (builtins.readFile ../scripts/kiosk-installer-api.py);

  installerGateScript = pkgs.writeShellScript "kiosk-installer-gate" ''
    set -uo pipefail
    export PATH="/run/current-system/sw/bin:$PATH"

    CONFIG_FILE="/etc/kiosk/config"
    IS_LIVE="no"

    if findmnt -n / | grep -qE 'tmpfs|overlay|squashfs|nix-store' 2>/dev/null; then
      IS_LIVE="yes"
    elif [ -d /iso ] || [ -d /nix/.ro-store ]; then
      IS_LIVE="yes"
    elif ! [ -f /etc/fstab ] || [ "$(wc -l < /etc/fstab 2>/dev/null)" -lt 2 ]; then
      IS_LIVE="yes"
    fi

    AUTO_INSTALL=""
    if [ -f "$CONFIG_FILE" ]; then
      AUTO_INSTALL=$(grep -E "^[[:space:]]*auto_install[[:space:]]*=" "$CONFIG_FILE" 2>/dev/null \
        | tail -n1 | cut -d'=' -f2- | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')
    fi

    CONFIG_SOURCE=$(cat /run/kiosk-config-source 2>/dev/null || printf 'default')

    # On live media the installer is the default entry point: without a config
    # of their own, users have no other way to reach it — a fresh boot has no
    # keyboard, no shell and no menu. A config from USB means the kiosk was
    # deliberately configured, so it boots straight into the browser.
    SHOW_INSTALLER="no"
    if [ "$IS_LIVE" = "yes" ]; then
      case "$AUTO_INSTALL" in
        yes) SHOW_INSTALLER="yes" ;;
        no)  SHOW_INSTALLER="no" ;;
        *)   [ "$CONFIG_SOURCE" = "usb" ] || SHOW_INSTALLER="yes" ;;
      esac
    fi

    if [ "$SHOW_INSTALLER" != "yes" ]; then
      echo "[kiosk-installer] Skipping installer (live=$IS_LIVE, config=$CONFIG_SOURCE, auto_install=''${AUTO_INSTALL:-unset})"
      exit 0
    fi

    echo "[kiosk-installer] Live media (config=$CONFIG_SOURCE, auto_install=''${AUTO_INSTALL:-unset}) -> starting installer"

    install -d -m 0755 /run/kiosk
    head -c 32 /dev/urandom | base64 | tr -d '/+=' | head -c 32 > /run/kiosk/installer-token
    chmod 0600 /run/kiosk/installer-token

    systemctl start kiosk-installer-api.service

    for _ in $(seq 1 50); do
      if curl -sf -m 1 http://127.0.0.1:8484/health >/dev/null 2>&1; then
        break
      fi
      sleep 0.2
    done

    echo "http://127.0.0.1:8484/" > /tmp/kiosk-homepage-override
  '';
in
{
  systemd.services.kiosk-installer-api = {
    description = "kiosk-os disk installer API";
    serviceConfig = {
      Type = "simple";
      ExecStart = "${pkgs.python3}/bin/python3 ${installerApi}";
      Restart = "on-failure";
    };
  };

  systemd.services.kiosk-installer-gate = {
    description = "Decide whether to show installer or homepage";
    wantedBy = [ "multi-user.target" ];
    before = [ "cage-tty1.service" ];
    after = [ "kiosk-config-fetcher.service" ];
    path = [ pkgs.curl pkgs.util-linux ];
    serviceConfig = {
      Type = "oneshot";
      RemainAfterExit = true;
      ExecStart = "${installerGateScript}";
    };
  };

  environment.etc."kiosk/installer.html" = {
    source = ../assets/installer.html;
    mode = "0644";
  };

  environment.systemPackages = with pkgs; [
    parted
    dosfstools
    e2fsprogs
    util-linux
    python3
    nixos-install-tools
  ];
}
