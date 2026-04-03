# kiosk-os Wizard

> **Status:** Planned — not yet implemented.

The wizard will be a cross-platform CLI/GUI tool that simplifies kiosk setup:

1. Select a USB stick
2. Configure network (wired/WiFi), homepage, and options interactively
3. Flash the kiosk-os image with the embedded configuration

## Planned Features

- Interactive CLI with arrow-key selection
- Optional web-based UI (localhost)
- Auto-detect connected USB drives
- Cross-platform: macOS, Linux, Windows
- Single binary (no runtime dependencies)

## Technology

TBD — candidates:

- **Go** + [Bubbletea](https://github.com/charmbracelet/bubbletea) (TUI)
- **Rust** + [Ratatui](https://github.com/ratatui-org/ratatui) (TUI)
- **Tauri** (Desktop GUI)

## Contributing

If you'd like to help build the wizard, check the [issues](../../issues) labeled `wizard`.
