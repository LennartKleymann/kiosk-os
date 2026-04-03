# Migrating from Porteus Kiosk

kiosk-os was designed as a drop-in replacement for Porteus Kiosk. Most configuration parameters are compatible.

## Why Migrate?

- Porteus Kiosk v6+ requires a **paid subscription** (€40/device/year)
- Without subscription, no security updates
- kiosk-os is **free and open-source** forever (GPL-3.0)

## Config Compatibility

Most Porteus Kiosk parameters work directly in kiosk-os:

| Porteus Kiosk | kiosk-os | Status |
|---|---|---|
| `homepage=` | `homepage=` | Compatible |
| `kiosk_config=` | `kiosk_config=` | Compatible |
| `connection=wired\|wifi` | `connection=wired\|wifi` | Compatible |
| `dhcp=yes\|no` | `dhcp=yes\|no` | Compatible |
| `disable_navigation_bar=yes` | `disable_navigation_bar=yes` | Compatible |
| `wallpaper=URL` | `wallpaper=URL` | Compatible |
| `primary_keyboard_layout=` | `primary_keyboard_layout=` | Compatible |
| `timezone=` | `timezone=` | Compatible |
| `wake_on_lan=yes` | — | Not yet supported |
| `additional_components=` | — | Not applicable (NixOS handles this) |
| `browser=firefox` | — | Chromium only (for now) |

## Migration Steps

1. **Copy your Porteus config** — The `kiosk_config=` URL and `homepage=` work the same way
2. **Flash kiosk-os** to your USB sticks
3. **Update the config URL** to point to the new config file format (if needed)
4. **Boot** — The kiosk should work immediately

## Key Differences

| Aspect | Porteus Kiosk | kiosk-os |
|---|---|---|
| Base OS | Gentoo-based | NixOS |
| Browser | Firefox or Chromium | Chromium |
| Display Server | X11 | Wayland (Cage) |
| Config delivery | USB or URL | USB or URL |
| Updates | Paid subscription | Free (NixOS rebuild) |
| License | Proprietary (v6+) | GPL-3.0 |
| Reproducibility | No | Yes (Nix flake) |
