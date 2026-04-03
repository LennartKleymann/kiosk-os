# Installation

## Requirements

- A USB stick (4 GB minimum, 8 GB recommended)
- An x86_64 PC (Intel or AMD) for the kiosk
- Network connection (Ethernet or WiFi)

## Quick Start

### 1. Download the ISO

Download the latest `kiosk-os.iso` from the [Releases](../../releases) page.

### 2. Flash to USB

**Linux / macOS:**
```bash
sudo dd if=kiosk-os.iso of=/dev/sdX bs=4M status=progress
sync
```

Replace `/dev/sdX` with your USB stick device. Use `lsblk` (Linux) or `diskutil list` (macOS) to find it.

**Windows:**

Use [Rufus](https://rufus.ie/) or [balenaEtcher](https://etcher.balena.io/):
1. Select `kiosk-os.iso`
2. Select your USB stick
3. Click "Flash"

### 3. Create a config file

Create a text file (e.g., `kiosk.conf`) with your settings:

```ini
homepage=https://your-webapp.com
connection=wired
dhcp=yes
timezone=Europe/Berlin
```

See [configuration.md](configuration.md) for all available parameters.

### 4. Add config to USB stick

After flashing, the USB stick has a partition labeled `KIOSK_CFG`. Copy your config file there as `kiosk.conf`.

Alternatively, host the config on a web server and set `kiosk_config=https://your-server.com/kiosk.conf` — the kiosk will fetch it on every boot.

### 5. Boot

1. Plug the USB stick into your kiosk PC
2. Set the BIOS to boot from USB (usually F12 or DEL during startup)
3. The kiosk boots directly into the fullscreen browser

## Updating the Config

### Remote config (recommended for multiple kiosks)

If you use `kiosk_config=https://...`, simply update the file on your web server. The kiosk fetches the latest config on every boot.

### Local config

Re-mount the USB stick on another PC, edit `kiosk.conf` on the `KIOSK_CFG` partition, plug it back in, and reboot the kiosk.

## Troubleshooting

### Kiosk shows "Service Unavailable"

The configured homepage is not reachable. Check:
- Network cable is connected
- WiFi credentials are correct
- The web server is running
- The URL in the config is correct

The kiosk retries automatically every 30 seconds.

### No display output

- Ensure the PC supports UEFI boot
- Try a different video output (HDMI, DisplayPort)
- Check BIOS settings: enable UEFI, disable Secure Boot

### WiFi not connecting

- Verify `wifi_ssid` and `wifi_password` in the config
- Ensure the WiFi network is 2.4 GHz or 5 GHz (not WiFi 6E only)
- Check if the WiFi adapter is supported (most Intel, Realtek, Broadcom adapters work)
