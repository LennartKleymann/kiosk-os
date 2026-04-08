{
  description = "kiosk-os — Open-source kiosk operating system based on NixOS";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-24.11";
  };

  outputs = { self, nixpkgs }: let
    targetSystem = "x86_64-linux";

    # Shared kiosk modules (used by both ISO and disk targets)
    kioskModules = [
      ./modules/default.nix
      ({ ... }: { system.stateVersion = "24.11"; })
    ];

    # Live ISO configuration — bootable installation media
    kioskIso = nixpkgs.lib.nixosSystem {
      system = targetSystem;
      modules = kioskModules ++ [
        "${nixpkgs}/nixos/modules/installer/cd-dvd/iso-image.nix"
        ({ pkgs, ... }: {
          isoImage = {
            isoName = "kiosk-os.iso";
            volumeID = "KIOSK_OS";
            makeEfiBootable = true;
            makeUsbBootable = true;
          };

          # Store the path to the pre-built disk system so the installer
          # can reference it via nixos-install --system <path>
          environment.etc."kiosk/disk-system".text =
            builtins.toString kioskDisk.config.system.build.toplevel;

          environment.systemPackages = with pkgs; [
            nixos-install-tools
          ];
        })
      ];
    };

    # Disk installation target — GRUB bootloader + filesystem labels
    kioskDisk = nixpkgs.lib.nixosSystem {
      system = targetSystem;
      modules = [
        "${nixpkgs}/nixos/modules/profiles/all-hardware.nix"
        ./modules/default.nix
        ({ ... }: { system.stateVersion = "24.11"; })
        ({ ... }: {
          boot.loader.grub = {
            enable = true;
            efiSupport = true;
            efiInstallAsRemovable = true;
            device = "nodev";
          };

          # Broad hardware support for common x86 systems
          boot.initrd.availableKernelModules = [
            "xhci_pci" "ahci" "nvme" "usbhid" "usb_storage" "sd_mod"
            "sr_mod" "virtio_pci" "virtio_blk" "ehci_pci" "uhci_hcd"
          ];

          # Filesystem mounts by label (set during partitioning)
          fileSystems."/" = {
            device = "/dev/disk/by-label/KIOSK_ROOT";
            fsType = "ext4";
          };
          fileSystems."/boot" = {
            device = "/dev/disk/by-label/KIOSK_EFI";
            fsType = "vfat";
          };
        })
      ];
    };
  in {
    nixosConfigurations = {
      kiosk = kioskIso;
      kiosk-disk = kioskDisk;
    };

    # ISO builds only work on Linux hosts (x86_64-linux / aarch64-linux)
    packages = nixpkgs.lib.genAttrs [ "x86_64-linux" "aarch64-linux" ] (buildSystem: {
      iso = kioskIso.config.system.build.isoImage;
      default = kioskIso.config.system.build.isoImage;
    });
  };
}
