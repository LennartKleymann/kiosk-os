{ config, lib, pkgs, ... }:

{
  networking = {
    hostName = "kiosk";
    useDHCP = true;

    # Wireless support (configured at runtime by config-fetcher)
    wireless = {
      enable = true;
      allowAuxiliaryImperativeNetworks = true;
      userControlled.enable = true;
    };

    # Firewall: block all inbound by default
    firewall = {
      enable = true;
      allowedTCPPorts = [ ];
      allowedUDPPorts = [ ];
    };
  };

  services.timesyncd.enable = true;
  time.timeZone = lib.mkDefault "Europe/Berlin";

  services.avahi = {
    enable = true;
    nssmdns4 = true;
  };
}
