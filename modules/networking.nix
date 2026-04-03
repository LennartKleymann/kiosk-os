{ config, lib, pkgs, ... }:

{
  # Basic networking
  networking = {
    hostName = "kiosk";
    useDHCP = true;

    # Wireless support (configured at runtime by config-fetcher)
    wireless = {
      enable = true;
      allowAuxiliaryImperativeNetworks = true;
      # Runtime config writes to /etc/wpa_supplicant.conf
      userControlled.enable = true;
    };

    # Firewall: only allow outbound, block all inbound by default
    firewall = {
      enable = true;
      allowedTCPPorts = [ ];
      allowedUDPPorts = [ ];
    };
  };

  # Time synchronization
  services.timesyncd.enable = true;
  time.timeZone = lib.mkDefault "Europe/Berlin";

  # mDNS for local network discovery
  services.avahi = {
    enable = true;
    nssmdns4 = true;
  };
}
