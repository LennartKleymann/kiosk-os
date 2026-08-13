# kiosk-os Wizard

Desktop tool that prepares a kiosk-os USB stick: configure the kiosk on your
PC, write the stick, take it to the device.

This exists because kiosks are often touch-only. Typing a WiFi password on a
machine with no keyboard is not practical, so the configuration is entered here
and travels on the stick.

## Status

Working towards a first release. Windows and Linux are the supported targets;
macOS device enumeration exists but the write path is not finished.

## What it does

1. **Configure** — homepage, browser mode, network (wired or WiFi), timezone,
   domain whitelist, idle timeouts
2. **Select a stick** — removable devices only, with size and model
3. **Write** — downloads the ISO from the latest GitHub release (or uses a
   local file), writes it, verifies it, then drops the generated `kiosk.conf`
   onto the `KIOSK_CFG` partition

The kiosk reads that partition on every boot
(see `modules/config-fetcher.nix`), so the same stick can be reconfigured
later by editing one text file.

## Requirements

- .NET 10 SDK
- Administrator (Windows) or root (Linux) — writing to a raw block device
  needs elevated rights

## Build and run

```bash
cd wizard/KioskOsWizard
dotnet run
```

## Architecture

Avalonia with MVVM (CommunityToolkit.Mvvm). Platform differences live behind
interfaces resolved at runtime:

| Piece | Purpose |
|---|---|
| `Models/KioskConfig.cs` | Serializes to the `key=value` format the kiosk parses |
| `Services/IUsbService.cs` | Device enumeration, one implementation per OS |
| `Services/FlashService.cs` | Raw block writes with progress and verification |
| `Services/GithubReleaseService.cs` | Release lookup and ISO download |
| `ViewModels/` | One per wizard step, orchestrated by `MainWindowViewModel` |

## Contributing

macOS support and code signing are the two open areas. See the
[issues](../../issues) labeled `wizard`.
