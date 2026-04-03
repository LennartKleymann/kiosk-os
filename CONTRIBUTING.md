# Contributing to kiosk-os

Thanks for considering a contribution! This document explains how to get started.

## Development Setup

1. Install [Nix](https://nixos.org/download.html) with flakes enabled
2. Clone the repository
3. Build the ISO: `nix build .#iso`
4. Test in a VM: `nix build .#vm` or use QEMU directly

## Commit Convention

We follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/).

### Format

```
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```

### Types

| Type | Description |
|---|---|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only |
| `style` | Formatting, no code change |
| `refactor` | Code restructuring, no feature change |
| `test` | Adding or updating tests |
| `chore` | Build process, CI, tooling |
| `perf` | Performance improvement |

### Scopes

| Scope | Description |
|---|---|
| `kiosk` | Core kiosk module (Cage, Chromium, session) |
| `config` | Config fetcher, parser, format |
| `network` | Networking (WLAN, LAN, DHCP) |
| `security` | Firewall, whitelist, USB block, keyboard lock |
| `power` | DPMS, idle detection, scheduled actions |
| `display` | Wallpaper, splash screen, mouse cursor |
| `admin` | SSH access, remote management |
| `build` | Flake, ISO build, CI pipeline |
| `wizard` | Setup wizard tool |
| `docs` | Documentation |

### Examples

```
feat(kiosk): add Cage + Chromium kiosk session
feat(network): add WiFi support via wpa_supplicant
fix(config): handle missing config URL gracefully
docs(config): add full parameter reference
chore(build): add GitLab CI pipeline
test(config): add config parser unit tests
```

## Merge Request Guidelines

- **Title**: Use conventional commit format (e.g., `feat(kiosk): add idle session reset`)
- **Language**: English for all code, commits, MRs, and documentation
- **Description**: Explain *what* and *why*, not *how*
- **Size**: Keep MRs focused — one feature or fix per MR
- **Tests**: Add or update tests when applicable
- **Docs**: Update documentation if behavior changes

## Branch Naming

```
feat/short-description
fix/short-description
docs/short-description
chore/short-description
```

## Code Style

- **Nix**: Follow [nixpkgs conventions](https://nixos.org/manual/nixpkgs/stable/#sec-contributing)
- **Shell scripts**: Use `shellcheck` and `set -euo pipefail`
- **Config files**: Use `key=value` format, `#` for comments

## Questions?

Open an issue or start a discussion. We're happy to help!
