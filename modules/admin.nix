{ config, lib, pkgs, ... }:

{
  # SSH is disabled by default
  # The config-fetcher enables it at runtime if admin_ssh=yes is set
  # TODO: revert to lib.mkDefault false after debugging
  services.openssh = {
    enable = true;
    settings = {
      PasswordAuthentication = true;
      PermitRootLogin = "yes";
    };
  };

  # Admin user for SSH access (only used when SSH is enabled)
  users.users.admin = {
    isNormalUser = true;
    extraGroups = [ "wheel" ];
    openssh.authorizedKeys.keys = [ ];
  };

  # TODO: remove after debugging — temporary root password for VM testing
  users.users.root.initialPassword = "kiosk";
}
