{
  description = "kiosk-os — Open-source kiosk operating system based on NixOS";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-24.11";
  };

  outputs = { self, nixpkgs }: let
    system = "x86_64-linux";
    pkgs = nixpkgs.legacyPackages.${system};

    kioskSystem = nixpkgs.lib.nixosSystem {
      inherit system;
      modules = [
        ./modules/default.nix
        "${nixpkgs}/nixos/modules/installer/cd-dvd/iso-image.nix"
        ({ config, lib, pkgs, ... }: {
          isoImage = {
            isoName = "kiosk-os.iso";
            volumeID = "KIOSK_OS";
            makeEfiBootable = true;
            makeUsbBootable = true;
          };

          system.stateVersion = "24.11";
        })
      ];
    };
  in {
    nixosConfigurations.kiosk = kioskSystem;

    packages.${system} = {
      iso = kioskSystem.config.system.build.isoImage;
      default = self.packages.${system}.iso;
    };
  };
}
