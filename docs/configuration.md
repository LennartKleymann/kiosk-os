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
| `whitelist` | domains (pipe-separated) | — | Only allow these domains. All others are blocked |
| `disable_navigation_bar` | `yes`, `no` | `no` | Hide the browser navigation bar |
| `disable_address_bar` | `yes`, `no` | `no` | Hide the browser address bar |
| `right_mouse_click` | `yes`, `no` | `yes` | Allow right-click context menu |

## Display

| Parameter | Values | Default | Description |
|---|---|---|---|
| `wallpaper` | URL | — | Wallpaper image URL, downloaded at boot |
| `hide_mouse` | seconds (integer) | `0` | Hide cursor after N seconds of inactivity. `0` = never |
| `timezone` | tz string | `Europe/Berlin` | System timezone |
| `primary_keyboard_layout` | layout code | `us` | Keyboard layout (e.g., `de`, `fr`, `es`) |

## Power Management

| Parameter | Values | Default | Description |
|---|---|---|---|
| `session_idle` | minutes (integer) | `0` | Reset browser session after N minutes of inactivity. `0` = disabled |
| `dpms_idle` | minutes (integer) | `0` | Turn off display after N minutes of inactivity. `0` = disabled. Any input wakes instantly |
| `scheduled_action` | see below | — | Execute commands on a schedule |

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

## Administration

| Parameter | Values | Default | Description |
|---|---|---|---|
| `admin_ssh` | `yes`, `no` | `no` | Enable SSH access for remote management |
| `admin_ssh_key` | SSH public key | — | Authorized SSH key for the admin user |
| `admin_ssh_port` | port number | `22` | SSH port |
