# Architecture

## Overview

kiosk-os is a minimal NixOS-based operating system that boots directly into a fullscreen web browser. It is designed to be immutable, reproducible, and remotely configurable.

## Boot Flow

```
Power On
  │
  ├─ BIOS/UEFI
  │
  ├─ GRUB (hidden, 0s timeout)
  │
  ├─ Linux Kernel + initramfs
  │
  ├─ Plymouth (splash screen with logo/wallpaper)
  │
  ├─ systemd
  │   │
  │   ├─ kiosk-config-fetcher.service    [oneshot, early boot]
  │   │   ├─ Read config from USB partition (KIOSK_CFG)
  │   │   ├─ Fetch remote config if kiosk_config= is set
  │   │   ├─ Apply WiFi settings
  │   │   ├─ Apply USB blocking rules
  │   │   └─ Write Chromium whitelist policy
  │   │
  │   ├─ cage-tty1.service               [main session]
  │   │   ├─ Auto-login as 'kiosk' user
  │   │   ├─ Start Cage (Wayland compositor)
  │   │   ├─ Start swaybg (wallpaper)
  │   │   └─ Start Chromium --kiosk (fullscreen browser)
  │   │
  │   └─ kiosk-idle-watcher.service      [background]
  │       ├─ Monitor user inactivity
  │       ├─ DPMS: turn off display after N minutes
  │       └─ Session reset: restart Cage after N minutes
  │
  └─ Ready (<15 seconds from power on)
```

## Components

### Cage (Wayland Compositor)

[Cage](https://github.com/cage-kiosk/cage) is a minimal Wayland compositor designed specifically for kiosk use. It runs a single application in fullscreen and prevents all window management (no Alt+Tab, no minimize, no window switching).

### Chromium (Kiosk Mode)

Chromium runs with these flags:
- `--kiosk` — Fullscreen, no UI chrome
- `--incognito` — No persistent data
- `--noerrdialogs` — Suppress error popups
- `--disable-extensions` — No extensions
- `--ozone-platform=wayland` — Native Wayland rendering

Browser lockdown is enforced via Chromium policies in `/etc/chromium/policies/managed/`.

### Config Fetcher

A systemd oneshot service that runs before the kiosk session:

1. Checks for config on USB partition (`KIOSK_CFG`)
2. If `kiosk_config=` URL is found, fetches remote config
3. Applies runtime settings (WiFi, USB rules, Chromium policies)
4. Writes parsed config to `/etc/kiosk/config`

### Idle Watcher

Uses `swayidle` to monitor user activity:
- **DPMS**: Turns off the display after configured idle time
- **Session reset**: Restarts the Cage service (and thus the browser) after configured idle time

## Security Model

- **Immutable base system**: NixOS `/nix/store` is read-only
- **No shell access**: Virtual consoles are disabled, no terminal emulator available
- **No Ctrl+Alt+Del**: Disabled via systemd
- **USB blocking**: udev rules prevent mass storage devices
- **Browser lockdown**: Chromium policies prevent dev tools, downloads, extensions
- **Whitelist**: Chromium URL policies restrict accessible domains
- **No sudo**: kiosk user has no elevated privileges

## NixOS Module Structure

```
modules/
├── default.nix          imports all modules
├── boot.nix             silent boot, Plymouth, kernel params
├── kiosk.nix            Cage + Chromium + kiosk user + session
├── display.nix          wallpaper (swaybg), fonts, Plymouth theme
├── networking.nix       DHCP, WiFi, firewall, mDNS
├── security.nix         USB block, Chromium policies, console lock
├── power.nix            swayidle, DPMS, session idle
├── config-fetcher.nix   config loading from USB/URL
└── admin.nix            optional SSH access
```

Each module is self-contained and can be individually enabled/disabled.
