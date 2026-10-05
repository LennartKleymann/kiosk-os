# Installation

## Requirements

- A USB stick (4 GB minimum, 8 GB recommended)
- An x86_64 PC (Intel or AMD) for the kiosk
- Network connection (Ethernet or WiFi)

## Quick Start with the Setup Wizard (Windows, Linux)

The wizard is the easiest way: it downloads the ISO, writes the stick, verifies it and puts your
configuration on it.

1. Download `KioskOsWizard.exe` (Windows) or `KioskOsWizard` (Linux) from the
   [Releases](../../releases) page
2. Start it as administrator (Windows) or with `sudo` (Linux)
3. Enter homepage, network (wired or WiFi with SSID and password), whitelist and timezone. Tick
   **Install kiosk-os on the internal disk** if the kiosk PC should run from its own disk
4. Pick the USB stick and write it
5. Plug the stick into the kiosk PC and boot from it (UEFI, Secure Boot off)

The configuration ends up as `kiosk.conf` on the stick's `KIOSK_CFG` partition and can be edited
there later with any text editor.

## Manual Setup

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

Use the setup wizard (above), [balenaEtcher](https://etcher.balena.io/) or
[Rufus](https://rufus.ie/). With Rufus, choose **DD image mode** when asked — in ISO mode the
`KIOSK_CFG` partition is lost.

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

## Installing to Internal Disk

By default, kiosk-os runs as a live system from the USB stick. To install it permanently to the internal disk:

1. Add `auto_install=yes` to your config file (the wizard does this when *Install kiosk-os on the
   internal disk* is ticked). A stick without any config shows the installer as well
2. Boot from USB
3. The installer UI appears in the browser instead of the homepage
4. Select the target disk — the installer shows the internal disks with model, size, and type.
   The stick itself is never offered
5. **Confirm** that you want to erase the selected disk (all data will be permanently deleted)
6. Wait for the installation to complete (progress is shown in the browser)
7. Pull the USB stick — the machine restarts from the internal disk on its own. Alternatively click
   **Reboot now** and leave the stick in: the internal disk is made the first UEFI boot entry

After installation, the kiosk boots directly from the internal disk with the configuration from the
stick (network, WiFi, homepage, …). The USB stick is only needed once.

### Updating the config after disk installation

The internal disk has a separate FAT32 partition labeled `KIOSK_CFG`. To update the config:

- **Remote config (recommended):** Set `kiosk_config=https://...` and update the file on your server
- **Manual:** Boot from another USB/system, mount the `KIOSK_CFG` partition, edit `kiosk.conf`

## Updating the Config (USB Live)

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

The kiosk retries automatically every 10 seconds and switches to the homepage as soon as it is reachable.

### No display output

- Ensure the PC supports UEFI boot
- Try a different video output (HDMI, DisplayPort)
- Check BIOS settings: enable UEFI, disable Secure Boot

### WiFi not connecting

- Verify `wifi_ssid` and `wifi_password` in the config (WPA passwords have 8–63 characters)
- Ensure the WiFi network is 2.4 GHz or 5 GHz (not WiFi 6E only)
- Check if the WiFi adapter is supported (most Intel, Realtek, Broadcom adapters work)
