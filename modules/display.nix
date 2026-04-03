{ config, lib, pkgs, ... }:

{
  # Plymouth boot splash
  boot.plymouth = {
    enable = true;
    # TODO: custom kiosk-os theme with logo
    # theme = "kiosk-os";
  };

  # Default wallpaper shipped with the image
  environment.etc."kiosk/wallpaper-default.jpg" = lib.mkIf (builtins.pathExists ../assets/wallpaper-default.jpg) {
    source = ../assets/wallpaper-default.jpg;
    mode = "0644";
  };

  # Fonts for browser rendering
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
