{ config, lib, pkgs, ... }:

{
  imports = [
    ./boot.nix
    ./kiosk.nix
    ./display.nix
    ./networking.nix
    ./security.nix
    ./power.nix
    ./config-fetcher.nix
    ./admin.nix
  ];
}
