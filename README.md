# kiosk-os

**Open-source kiosk operating system built on NixOS.**

Turn any x86 PC into a locked-down web kiosk in under 5 minutes. Boot from USB, show a fullscreen browser, manage everything via a simple config file. No setup wizards, no login screens, no bloat.

## Why?

Existing kiosk solutions like [Porteus Kiosk](https://porteus-kiosk.org/) have moved to paid subscription models. **kiosk-os** is a free, open-source alternative that gives you full control over your kiosk fleet.

## Features

- **Zero-config boot** — Flash USB, plug in, power on, done
- **Remote configuration** — Manage all kiosks from a single config URL
- **WiFi & Ethernet** — Automatic network setup via config
- **Domain whitelist** — Restrict browser to approved domains only
- **Session auto-reset** — Browser resets after configurable idle time
- **Display power management** — Screen turns off after inactivity, instant wake on touch
- **USB lockdown** — Block mass storage devices (keyboards/mice still work)
- **Custom wallpaper** — Boot splash and background from URL
- **Silent boot** — No GRUB menu, no kernel messages, just your logo
- **SSH admin access** — Optional remote management via SSH key
- **Automatic security updates** — NixOS handles updates declaratively
- **Fully reproducible** — Same config always builds the exact same image

## Tech Stack

| Component | Choice |
|---|---|
| Base OS | [NixOS](https://nixos.org) |
| Display Server | [Cage](https://github.com/cage-kiosk/cage) (Wayland) |
| Browser | [Chromium](https://www.chromium.org/) (kiosk mode) |
| Boot Splash | [Plymouth](https://www.freedesktop.org/wiki/Software/Plymouth/) |
| Wallpaper | [swaybg](https://github.com/swaywm/swaybg) |

## Quick Start

### 1. Download

Grab the latest ISO from the [Releases](../../releases) page.

### 2. Create a config file

```ini
homepage=https://your-webapp.com
connection=wired
dhcp=yes
timezone=Europe/Berlin
```

See [configs/full.conf](configs/full.conf) for all available options.

### 3. Flash to USB

```bash
sudo dd if=kiosk-os.iso of=/dev/sdX bs=4M status=progress
```

### 4. Boot

Plug the USB stick into your kiosk PC, power on. That's it.

## Configuration

kiosk-os uses a simple `key=value` config format. The config can be:

- **Embedded on the USB stick** (written by the setup wizard)
- **Fetched from a URL** on every boot (for fleet management)

```ini
# Network
connection=wifi
wifi_ssid=OfficeWiFi
wifi_password=Secret123

# Browser
homepage=https://app.example.com
whitelist=app.example.com|cdn.example.com

# Security
disable_navigation_bar=yes
removable_devices=no

# Power
session_idle=10
dpms_idle=60
```

Full parameter reference: [docs/configuration.md](docs/configuration.md)

## Building from Source

Requires [Nix](https://nixos.org/download.html) with flakes enabled.

```bash
git clone https://github.com/lennart-kleymann/kiosk-os.git
cd kiosk-os
nix build .#iso
```

The ISO will be at `result/iso/kiosk-os.iso`.

For testing in a VM (e.g., QEMU, UTM, VirtualBox):

```bash
qemu-system-x86_64 -cdrom result/iso/kiosk-os.iso -m 2G -enable-kvm
```

## Project Structure

```
modules/          NixOS modules (core system configuration)
scripts/          Runtime scripts (config parser, idle watcher, health check)
wizard/           Setup wizard (planned)
configs/          Example configuration files
docs/             Documentation
assets/           Splash screen, default wallpaper, error page
tests/            Automated tests
```

## Migrating from Porteus Kiosk

kiosk-os uses a configuration format compatible with Porteus Kiosk. Most parameters work the same way. See [docs/migration-porteus.md](docs/migration-porteus.md) for a detailed migration guide.

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for commit conventions, branch naming, and MR guidelines.

## License

[AGPL-3.0](LICENSE)
