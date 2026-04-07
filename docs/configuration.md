# Configuration Reference

kiosk-os uses a simple `key=value` configuration format. Lines starting with `#` are comments.

## Config Loading Order

1. **USB partition** — If a partition labeled `KIOSK_CFG` exists on the boot drive, the file `kiosk.conf` is read from it
2. **Remote URL** — If `kiosk_config=` is set, the remote config is fetched and overrides the local config

## Network

| Parameter | Values | Default | Description |
|---|---|---|---|
| `connection` | `wired`, `wifi` | `wired` | Network connection type |
| `dhcp` | `yes`, `no` | `yes` | Use DHCP for IP assignment |
| `wifi_ssid` | string | — | WiFi network name (required when `connection=wifi`) |
| `wifi_password` | string | — | WiFi password |
| `ip` | IP address | — | Static IP (only when `dhcp=no`) |
| `netmask` | netmask | `255.255.255.0` | Subnet mask |
| `gateway` | IP address | — | Default gateway |
| `dns` | IP address | — | DNS server |

## Browser

| Parameter | Values | Default | Description |
|---|---|---|---|
| `homepage` | URL | `https://example.com` | **Required.** The URL shown in the kiosk browser |
| `browser_mode` | `kiosk`, `fullscreen` | `kiosk` | Browser UI mode (see below) |
| `whitelist` | domains (pipe-separated) | — | Only allow these domains. All others are blocked |
| `right_mouse_click` | `yes`, `no` | `yes` | *Planned.* Allow right-click context menu |

### Browser Modes

| Mode | UI Elements | Escape possible? | Use case |
|---|---|---|---|
| `kiosk` | Nothing — no toolbar, no address bar, no buttons | No | Locked-down public terminals |
| `fullscreen` | Full toolbar (back, forward, address bar) in fullscreen | Yes (F11) | Internal apps where navigation is needed |

**`kiosk`** (default): Chromium runs in true kiosk mode. No UI elements, no way to exit fullscreen. The user can only interact with the web page. Best for public-facing terminals.

**`fullscreen`**: Chromium runs in fullscreen with the full toolbar visible (back button, forward button, address bar). The whitelist still protects against navigating to unauthorized domains — the address bar is visible but restricted. Tabs, bookmarks, downloads, and developer tools are disabled via Chromium policies.

## Display

| Parameter | Values | Default | Description |
|---|---|---|---|
| `wallpaper` | URL | — | Wallpaper image URL, downloaded at boot |
| `hide_mouse` | seconds (integer) | `0` | *Planned.* Hide cursor after N seconds of inactivity |
| `timezone` | tz string | `Europe/Berlin` | System timezone |
| `primary_keyboard_layout` | layout code | `us` | *Planned.* Keyboard layout (e.g., `de`, `fr`, `es`) |

## Power Management

| Parameter | Values | Default | Description |
|---|---|---|---|
| `session_idle` | minutes (integer) | `0` | Reset browser session after N minutes of inactivity. `0` = disabled |
| `dpms_idle` | minutes (integer) | `0` | Turn off display after N minutes of inactivity. `0` = disabled. Any input wakes instantly |
| `scheduled_action` | see below | — | *Planned.* Execute commands on a schedule |

### Scheduled Actions

Format: `Day-HH:MM action:command`

Multiple days separated by spaces. The command is a shell command.

```ini
# Shutdown every weekday at 18:00
scheduled_action=Monday-18:00 Tuesday-18:00 Wednesday-18:00 Thursday-18:00 Friday-18:00 action:shutdown

# Reboot every day at 03:00
scheduled_action=Monday-03:00 Tuesday-03:00 Wednesday-03:00 Thursday-03:00 Friday-03:00 Saturday-03:00 Sunday-03:00 action:reboot
```

## Security

| Parameter | Values | Default | Description |
|---|---|---|---|
| `removable_devices` | `yes`, `no` | `yes` | Allow USB mass storage devices. Keyboards and mice always work |
| `shutdown_menu` | `yes`, `no` | `yes` | Allow Ctrl+Alt+Del shutdown menu |

## Remote Configuration

| Parameter | Values | Default | Description |
|---|---|---|---|
| `kiosk_config` | URL | — | Remote config URL. Fetched on every boot. Overrides local config |

## Installation

| Parameter | Values | Default | Description |
|---|---|---|---|
| `auto_install` | `yes`, `no` | `no` | Show disk installer when booting from live USB |

When `auto_install=yes` is set and the system detects it is running from a live USB stick, a browser-based installer UI is shown instead of the homepage. The installer:

1. Detects all available internal disks
2. Shows disk details (model, size, type) and warns about removable devices
3. **Requires explicit confirmation** before erasing any disk
4. Partitions the disk (EFI + root + config partition)
5. Copies the kiosk-os system to the internal disk
6. Shows real-time installation progress
7. Prompts to remove the USB stick and reboot

After installation, the system boots from the internal disk and goes directly to the configured homepage. The USB stick is no longer needed.

The config partition (`KIOSK_CFG`) on the internal disk is a separate FAT32 partition that can be mounted from another system to update the configuration.

## Administration

| Parameter | Values | Default | Description |
|---|---|---|---|
| `admin_ssh` | `yes`, `no` | `no` | Enable SSH access for remote management |
| `admin_ssh_key` | SSH public key | — | Authorized SSH key for the admin user |
| `admin_ssh_port` | port number | `22` | *Planned.* SSH port |
