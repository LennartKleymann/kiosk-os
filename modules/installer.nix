{ config, lib, pkgs, ... }:

let
  installerApi = pkgs.writeScript "kiosk-installer-api" (builtins.readFile ../scripts/kiosk-installer-api.py);

  # Decides: show installer UI or go straight to homepage
  installerGateScript = pkgs.writeShellScript "kiosk-installer-gate" ''
    set -euo pipefail

    CONFIG_FILE="/etc/kiosk/config"
    AUTO_INSTALL="no"
    IS_LIVE="no"

    # Check if running from live media
    # NixOS live ISOs: root is tmpfs, or /iso exists, or no /etc/NIXOS_LUSTRATE
    if findmnt -n / | grep -qE 'tmpfs|overlay|squashfs|nix-store' 2>/dev/null; then
      IS_LIVE="yes"
    elif [ -d /iso ] || [ -d /nix/.ro-store ]; then
      IS_LIVE="yes"
    elif ! [ -f /etc/fstab ] || [ "$(wc -l < /etc/fstab 2>/dev/null)" -lt 2 ]; then
      # Live systems typically have empty or minimal fstab
      IS_LIVE="yes"
    fi

    # Read config
    if [ -f "$CONFIG_FILE" ]; then
      AUTO_INSTALL=$(grep -E "^auto_install=" "$CONFIG_FILE" | cut -d'=' -f2- | xargs || echo "no")
    fi

    if [ "$IS_LIVE" = "yes" ] && [ "$AUTO_INSTALL" = "yes" ]; then
      echo "[kiosk-installer] Live system + auto_install=yes -> starting installer"
      # Start Python API server in background
      ${pkgs.python3}/bin/python3 ${installerApi} &
      # Override homepage to show installer
      echo "file:///etc/kiosk/installer.html" > /tmp/kiosk-homepage-override
    else
      echo "[kiosk-installer] Skipping installer (live=$IS_LIVE, auto_install=$AUTO_INSTALL)"
    fi
  '';
in
{
  # Installer gate runs after config is loaded but before the kiosk session
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

  # Ship installer HTML
  environment.etc."kiosk/installer.html" = {
    source = ../assets/installer.html;
    mode = "0644";
  };

  # Packages needed for installation
  environment.systemPackages = with pkgs; [
    parted
    dosfstools
    e2fsprogs
    util-linux
    python3
    nixos-install-tools
  ];
}
