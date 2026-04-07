{ config, lib, pkgs, ... }:

let
  installerApi = pkgs.writeScript "kiosk-installer-api" (builtins.readFile ../scripts/kiosk-installer-api.py);

  installerGateScript = pkgs.writeShellScript "kiosk-installer-gate" ''
    set -euo pipefail
    export PATH="/run/current-system/sw/bin:$PATH"

    CONFIG_FILE="/etc/kiosk/config"
    AUTO_INSTALL="no"
    IS_LIVE="no"

    # Detect live media
    if findmnt -n / | grep -qE 'tmpfs|overlay|squashfs|nix-store' 2>/dev/null; then
      IS_LIVE="yes"
    elif [ -d /iso ] || [ -d /nix/.ro-store ]; then
      IS_LIVE="yes"
    elif ! [ -f /etc/fstab ] || [ "$(wc -l < /etc/fstab 2>/dev/null)" -lt 2 ]; then
      IS_LIVE="yes"
    fi

    # Read config
    if [ -f "$CONFIG_FILE" ]; then
      AUTO_INSTALL=$(grep -E "^auto_install=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs || echo "no")
    fi

    if [ "$IS_LIVE" = "yes" ] && [ "$AUTO_INSTALL" = "yes" ]; then
      echo "[kiosk-installer] Live system + auto_install=yes -> starting installer"
      ${pkgs.python3}/bin/python3 ${installerApi} &
      echo "file:///etc/kiosk/installer.html" > /tmp/kiosk-homepage-override
    else
      echo "[kiosk-installer] Skipping installer (live=$IS_LIVE, auto_install=$AUTO_INSTALL)"
    fi
  '';
in
{
  systemd.services.kiosk-installer-gate = {
    description = "Decide whether to show installer or homepage";
    wantedBy = [ "multi-user.target" ];
    before = [ "cage-tty1.service" ];
    after = [ "kiosk-config-fetcher.service" ];
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
