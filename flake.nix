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
    plainIso = kioskIso.config.system.build.isoImage;

    # Appends an empty FAT partition labelled KIOSK_CFG to the hybrid image.
    # Once flashed, the host OS mounts it like any other volume, so the setup
    # wizard only has to drop a text file onto it — no partitioning on the
    # host side, on any platform. An empty partition changes nothing at boot:
    # the config fetcher falls back to the default config.
    withConfigPartition = buildSystem: let
      pkgs = nixpkgs.legacyPackages.${buildSystem};
      cfgSizeMiB = 16;
    in pkgs.runCommand "kiosk-os-iso" {
      nativeBuildInputs = with pkgs; [ bash dosfstools util-linux coreutils jq ];
    } ''
      mkdir -p $out/iso
      iso=$out/iso/kiosk-os.iso
      cp ${plainIso}/iso/kiosk-os.iso $iso
      chmod +w $iso

      bash ${./scripts/append-config-partition.sh} $iso ${toString cfgSizeMiB}
      sfdisk --list $iso
    '';
  in {
    nixosConfigurations = {
      kiosk = kioskIso;
      kiosk-disk = kioskDisk;
    };

    # ISO builds only work on Linux hosts (x86_64-linux / aarch64-linux)
    packages = nixpkgs.lib.genAttrs [ "x86_64-linux" "aarch64-linux" ] (buildSystem: {
      iso = withConfigPartition buildSystem;
      iso-plain = plainIso;
      default = withConfigPartition buildSystem;
    });
  };
}
