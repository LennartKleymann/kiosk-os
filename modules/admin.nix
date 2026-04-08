{ lib, ... }:

{
  # SSH is disabled by default.
  # Runtime SSH configuration via config file is planned (see Roadmap in docs).
  services.openssh = {
    enable = lib.mkDefault false;
    settings = {
      PasswordAuthentication = false;
      PermitRootLogin = "no";
    };
  };
}
