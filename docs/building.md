# Building from Source

## Prerequisites

- [Nix](https://nixos.org/download.html) with flakes enabled
- A Linux host (native or via Docker)

### Enable Flakes

Add to `~/.config/nix/nix.conf`:

```
experimental-features = nix-command flakes
```

## Building the ISO

### On a Linux host

```bash
git clone https://github.com/lennart-kleymann/kiosk-os.git
cd kiosk-os
nix build .#iso
```

The ISO will be at `result/iso/kiosk-os.iso`.

### On macOS (via Docker)

Since NixOS images target x86_64-linux, macOS users need Docker:

```bash
docker run --rm --platform linux/amd64 \
  --security-opt seccomp=unconfined \
  -v "$(pwd):/build" -w /build \
  nixos/nix:latest bash -c "
    echo 'experimental-features = nix-command flakes' >> /etc/nix/nix.conf
    echo 'sandbox = false' >> /etc/nix/nix.conf
    nix build .#iso --accept-flake-config
  "
```

## Flashing to USB

```bash
sudo dd if=result/iso/kiosk-os.iso of=/dev/sdX bs=4M status=progress
sync
```

## Development

### Testing in a VM

For quick iteration, build and run a QEMU VM:

```bash
# On Linux
nix build .#iso
qemu-system-x86_64 -cdrom result/iso/kiosk-os.iso -m 2G -enable-kvm

# Without KVM (slower)
qemu-system-x86_64 -cdrom result/iso/kiosk-os.iso -m 2G
```

### Nix REPL

Explore the configuration interactively:

```bash
nix repl .#nixosConfigurations.kiosk
```

### Updating nixpkgs

```bash
nix flake update
```

This updates `flake.lock` to the latest nixpkgs revision.
