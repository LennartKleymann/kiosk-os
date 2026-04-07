{ config, lib, pkgs, ... }:

{
  boot.plymouth.enable = true;

  # Default wallpaper shipped with the image
  environment.etc."kiosk/wallpaper-default.jpg" = lib.mkIf (builtins.pathExists ../assets/wallpaper-default.jpg) {
    source = ../assets/wallpaper-default.jpg;
    mode = "0644";
  };

  fonts = {
    packages = with pkgs; [
      noto-fonts
      noto-fonts-emoji
    ];
    fontconfig.defaultFonts = {
      sansSerif = [ "Noto Sans" ];
      serif = [ "Noto Serif" ];
    };
  };
}
