{ ... }:

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

  # null keeps /etc/localtime writable so the config file can set the
  # timezone at runtime via timedatectl. A fixed value makes it read-only.
  time.timeZone = null;

  services.avahi = {
    enable = true;
    nssmdns4 = true;
  };
}
