{
  description = "kiosk-os — Open-source kiosk operating system based on NixOS";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-24.11";
  };

  outputs = { self, nixpkgs }: let
    # The kiosk image is always x86_64-linux
    targetSystem = "x86_64-linux";

    kioskSystem = nixpkgs.lib.nixosSystem {
      system = targetSystem;
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

    # Expose packages for all common build hosts
    forAllSystems = nixpkgs.lib.genAttrs [
      "x86_64-linux"
      "aarch64-linux"
      "aarch64-darwin"
      "x86_64-darwin"
    ];
  in {
    nixosConfigurations.kiosk = kioskSystem;

    packages = forAllSystems (buildSystem: {
      iso = kioskSystem.config.system.build.isoImage;
      default = kioskSystem.config.system.build.isoImage;
    });
  };
}
