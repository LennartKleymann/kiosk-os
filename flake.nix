{
  description = "kiosk-os — Open-source kiosk operating system based on NixOS";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-24.11";
  };

  outputs = { self, nixpkgs }: let
    targetSystem = "x86_64-linux";

    # Shared kiosk modules (used by both ISO and disk)
    kioskModules = [
      ./modules/default.nix
      ({ lib, ... }: { system.stateVersion = "24.11"; })
    ];

    # Live ISO configuration
    kioskIso = nixpkgs.lib.nixosSystem {
      system = targetSystem;
      modules = kioskModules ++ [
        "${nixpkgs}/nixos/modules/installer/cd-dvd/iso-image.nix"
        ({ config, lib, pkgs, ... }: {
          isoImage = {
            isoName = "kiosk-os.iso";
            volumeID = "KIOSK_OS";
            makeEfiBootable = true;
            makeUsbBootable = true;
          };

          # Store the path to the disk system closure for the installer
          # Instead of shipping the flake, we pre-build the disk system
          # and pass its store path to the installer
          environment.etc."kiosk/disk-system".text =
            builtins.toString kioskDisk.config.system.build.toplevel;

          # Include nixos-install tools
          environment.systemPackages = with pkgs; [
            nixos-install-tools
          ];
        })
      ];
    };

    # Disk installation configuration
    # hardware-configuration.nix is generated at install time by nixos-generate-config
    kioskDisk = nixpkgs.lib.nixosSystem {
      system = targetSystem;
      modules = [
        "${nixpkgs}/nixos/modules/profiles/all-hardware.nix"
        ./modules/default.nix
        ({ lib, ... }: { system.stateVersion = "24.11"; })
        ({ config, lib, pkgs, modulesPath, ... }: {
          # Bootloader for disk — use GRUB for maximum compatibility
          boot.loader.grub = {
            enable = true;
            efiSupport = true;
            efiInstallAsRemovable = true;
            device = "nodev";
          };

          # Common hardware support (covers most x86 machines)
          boot.initrd.availableKernelModules = [
            "xhci_pci" "ahci" "nvme" "usbhid" "usb_storage" "sd_mod"
            "sr_mod" "virtio_pci" "virtio_blk" "ehci_pci" "uhci_hcd"
          ];

          # Filesystem mounts by label (set during partitioning)
          fileSystems."/" = { device = "/dev/disk/by-label/KIOSK_ROOT"; fsType = "ext4"; };
          fileSystems."/boot" = { device = "/dev/disk/by-label/KIOSK_EFI"; fsType = "vfat"; };
        })
      ];
    };

    forAllSystems = nixpkgs.lib.genAttrs [
      "x86_64-linux"
      "aarch64-linux"
      "aarch64-darwin"
      "x86_64-darwin"
    ];
  in {
    nixosConfigurations = {
      kiosk = kioskIso;
      kiosk-disk = kioskDisk;
    };

    packages = forAllSystems (buildSystem: {
      iso = kioskIso.config.system.build.isoImage;
      default = kioskIso.config.system.build.isoImage;
    });
  };
}
