{ config, lib, pkgs, ... }:

{
  # sshd is installed but not started at boot.
  # The config-fetcher starts it at runtime if admin_ssh=yes and admin_ssh_key are set.
  services.openssh = {
    enable = true;
    openFirewall = true;
    settings = {
      PasswordAuthentication = false;
      KbdInteractiveAuthentication = false;
      PermitRootLogin = "no";
      AllowUsers = [ "admin" ];
    };
  };
  systemd.services.sshd.wantedBy = lib.mkForce [ ];

  # Admin user for SSH access (key is written at runtime from the config)
  users.users.admin = {
    isNormalUser = true;
    extraGroups = [ "wheel" ];
  };

  # The admin has no password (key-only login), so sudo must not ask for one.
  # The kiosk user is not in wheel and cannot use sudo.
  security.sudo = {
    enable = true;
    wheelNeedsPassword = false;
    execWheelOnly = true;
  };
}
