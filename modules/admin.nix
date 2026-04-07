{ config, lib, pkgs, ... }:

{
  # SSH is disabled by default
  # The config-fetcher enables it at runtime if admin_ssh=yes is set
  services.openssh = {
    enable = lib.mkDefault false;
    settings = {
      PasswordAuthentication = false;
      PermitRootLogin = "no";
    };
  };

  # Admin user for SSH access (only active when SSH is enabled)
  users.users.admin = {
    isNormalUser = true;
    extraGroups = [ "wheel" ];
    openssh.authorizedKeys.keys = [ ];
  };
}
