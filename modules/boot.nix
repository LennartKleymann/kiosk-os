{ config, lib, pkgs, ... }:

{
  # Silent boot — no GRUB menu, no kernel messages, just splash
  boot = {
    loader = {
      timeout = lib.mkForce 0;
      grub = {
        splashImage = null;
      };
    };

    consoleLogLevel = 0;
    initrd.verbose = false;
    kernelParams = [
      "quiet"
      "splash"
      "loglevel=0"
      "rd.systemd.show_status=false"
      "rd.udev.log_level=3"
      "udev.log_priority=3"
      "vt.global_cursor_default=0"
    ];

    plymouth = {
      enable = true;
    };
  };
}
