# Configuration Reference

kiosk-os uses a simple `key=value` configuration format. Lines starting with `#` are comments.
The file may be saved with any editor — Windows line endings (CRLF) and a UTF-8 BOM are handled.

## Config Loading Order

1. **Local config** — `kiosk.conf` on the partition labeled `KIOSK_CFG`. On a live stick that is
   the stick's own config partition (written by the setup wizard); on an installed kiosk it is the
   config partition of the internal disk. If several `KIOSK_CFG` partitions are present (for example
   the stick is still plugged into an installed kiosk), the one on the boot device wins.
2. **Network** is set up from the local config.
3. **Remote config** — if `kiosk_config=` is set, the remote file is fetched and laid over the local
   config **key by key**: a key in the remote file overrides the local value, keys the remote file
   does not mention keep their local value.
4. Everything else (timezone, whitelist, …) is applied from the merged result.

Without a local config the built-in default (`configs/default.conf`) is used.

> **Security note:** `auto_install` is stripped from remote configs. It can only be set via the
> local config, so a remote config can never trigger disk installations across a fleet. Network
> settings are always taken from the local config, so a remote file cannot cut a kiosk off its
> own network.

## Network

| Parameter | Values | Default | Description |
|---|---|---|---|
| `connection` | `wired`, `wifi` | `wired` | Network connection type |
| `wifi_ssid` | string | — | WiFi network name (required when `connection=wifi`). Hidden networks work too |
| `wifi_password` | string | — | WPA/WPA2 password, 8–63 characters. Leave empty for an open network |
| `dhcp` | `yes`, `no` | `yes` | Use DHCP. With `no`, the static settings below are used |
| `ip` | IPv4 address | — | Static address (`dhcp=no`) |
| `netmask` | netmask | `255.255.255.0` | Static netmask |
| `gateway` | IPv4 address | — | Default gateway |
| `dns` | IPv4 address(es) | — | DNS server(s), separated by `,` or `|` |

## Browser

| Parameter | Values | Default | Description |
|---|---|---|---|
| `homepage` | URL | `https://example.com` | **Required.** The URL shown in the kiosk browser |
| `browser_mode` | `kiosk`, `fullscreen` | `kiosk` | Browser UI mode (see below) |
| `whitelist` | domains (pipe-separated) | — | Only allow these domains. All others are blocked |

If the homepage cannot be reached at startup (no network yet, server down), a local
"Service Unavailable" page is shown. It checks the homepage every 10 seconds and switches to it as
soon as it answers.

### Browser Modes

| Mode | UI Elements | Use case |
|---|---|---|
| `kiosk` | Nothing — no toolbar, no address bar, no buttons | Locked-down public terminals |
| `fullscreen` | Toolbar with back/forward, reload, home and address bar, filling the screen | Internal apps where navigation is needed |

**`kiosk`** (default): Chromium runs in true kiosk mode. The user can only interact with the web page.

**`fullscreen`**: the browser window fills the screen and shows its toolbar. The whitelist still
applies to everything typed into the address bar. Downloads, developer tools, extensions and the
password manager are disabled via Chromium policies; browsing always runs in incognito mode.

## Display

| Parameter | Values | Default | Description |
|---|---|---|---|
| `wallpaper` | URL | — | Wallpaper image URL, downloaded at boot |
| `timezone` | tz string | `Europe/Berlin` | System timezone, e.g. `America/Chicago` |
| `primary_keyboard_layout` | XKB layout code | `us` | Keyboard layout: `us`, `gb`, `de`, `fr`, `es`, … — a layout code, not a language (`en` is read as `us`). Unknown values fall back to `us`, so a typo never disables the keyboard |

## Power Management

| Parameter | Values | Default | Description |
|---|---|---|---|
| `session_idle` | minutes (integer) | `0` | Restart the browser (back to the homepage, session data cleared) after N minutes without input. Only happens after someone actually used the kiosk. `0` = disabled |
| `dpms_idle` | minutes (integer) | `0` | Turn the display off after N minutes without input. Any input turns it back on. `0` = disabled |

## Security

| Parameter | Values | Default | Description |
|---|---|---|---|
| `removable_devices` | `yes`, `no` | `yes` | `no` blocks USB storage devices plugged in after boot. Keyboards, mice and touchscreens keep working, and so does the stick the kiosk booted from |

## Remote Configuration

| Parameter | Values | Default | Description |
|---|---|---|---|
| `kiosk_config` | URL | — | Remote config URL, fetched on every boot (retried for about 30 seconds while the network comes up). **Must be HTTPS** — plain HTTP is refused |

## Administration

| Parameter | Values | Default | Description |
|---|---|---|---|
| `admin_ssh` | `yes`, `no` | `no` | Start an SSH server for the user `admin` |
| `admin_ssh_key` | SSH public key | — | Key that may log in as `admin` (key login only, no passwords). `admin` may use `sudo` |

## Installation

| Parameter | Values | Default | Description |
|---|---|---|---|
| `auto_install` | `yes`, `no` | unset | Show the disk installer when booting from the live stick |

On a live stick the installer appears when `auto_install=yes` — the setup wizard sets this when
*Install kiosk-os on the internal disk* is ticked. A stick without any config also shows the
installer; a stick with a config but without `auto_install` boots straight into the browser.

The installer:

1. Lists the internal disks (never the stick it booted from)
2. **Requires explicit confirmation** before erasing the selected disk
3. Creates three partitions: `KIOSK_EFI` (512 MB), `KIOSK_ROOT`, `KIOSK_CFG` (256 MB)
4. Copies the system and installs the bootloader, showing real-time progress
5. Copies the stick's `kiosk.conf` (without `auto_install`) to the disk's `KIOSK_CFG` partition
6. Makes the internal disk the first UEFI boot entry

When it is done, either **pull the stick** — the machine restarts from the internal disk on its
own — or click **Reboot now** and leave the stick in; the internal disk is started first either way.

To change the configuration of an installed kiosk, mount its `KIOSK_CFG` partition from another
system and edit `kiosk.conf`, or use a remote config.

## Roadmap

The following parameters are planned for future releases:

- `hide_mouse` — Hide cursor after N seconds of inactivity
- `scheduled_action` — Scheduled shutdown / reboot
- `admin_ssh_port` — Custom SSH port
