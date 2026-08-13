# Testing kiosk-os in a VM

Manual test plan for a release candidate. Written for Hyper-V, but the
scenarios apply to any hypervisor.

## VM setup (Hyper-V)

| Setting | Value | Why |
|---|---|---|
| Generation | 2 | The ISO is built with `makeEfiBootable`. Gen 1 is legacy BIOS. |
| Secure Boot | **off** | Mandatory. NixOS is not signed with the Microsoft key and Gen 2 enables Secure Boot by default. This is the single most common reason the VM shows "boot failed". |
| Memory | 4 GB, static | The live system runs from RAM. Dynamic memory can starve it mid-install. |
| Disk | 20 GB VHDX | The installed system needs well over 8 GB. |
| CPU | 2+ | |
| Network | Default Switch | Needed for the remote-config test. |

Attach the ISO as a virtual DVD drive and put DVD first in the boot order.

Before each scenario, **reset the VM to a clean disk** — either a fresh
checkpoint or a recreated VHDX. Several scenarios only behave correctly on an
empty disk.

## Scenario 1 — Fresh boot, nothing configured

The case that matters most: someone downloads the ISO and boots it. Nothing
else was prepared.

1. Boot the ISO on an empty disk.
2. Expected: splash, then the **installer** appears in fullscreen.

Check:
- [ ] The installer comes up on its own, without any keyboard input
- [ ] Everything is reachable with the mouse alone — no field needs typing
- [ ] The disk list shows the 20 GB VHDX
- [ ] The disk list does **not** show the boot medium (no DVD, no ISO entry)

This is the scenario that was broken: the installer only started with
`auto_install=yes`, so a fresh boot went straight to the browser instead.

## Scenario 2 — Install to disk, then boot from it

1. From scenario 1, select the VHDX and confirm.
2. Watch the progress through partition → format → copy → bootloader → config.
3. On completion, reboot and **remove the ISO from the boot order**.

Check:
- [ ] Installation finishes without an error state
- [ ] The VM boots from the VHDX unaided (GRUB, no boot menu delay)
- [ ] The installed system starts the **browser**, not the installer again
- [ ] `KIOSK_EFI`, `KIOSK_ROOT`, `KIOSK_CFG` exist on the disk

The last check matters because the `fileSystems` entries mount by label. A
mislabelled partition means the installed system will not boot.

## Scenario 3 — Live boot with a config on USB

Proves that a deliberately configured kiosk does not get the installer.

Prepare a second small VHDX with a FAT32 partition labelled `KIOSK_CFG`
containing `kiosk.conf`:

```ini
homepage=https://example.com
connection=wired
dhcp=yes
timezone=America/Chicago
```

1. Attach it alongside the ISO and boot on an empty target disk.

Check:
- [ ] The installer does **not** appear
- [ ] The browser opens `https://example.com` directly
- [ ] The timezone is applied (a page showing local time is the quickest proof)

The timezone check is worth doing carefully — it silently never worked before,
because it symlinked from `/usr/share/zoneinfo`, which does not exist on NixOS.

## Scenario 4 — Config switches the installer off

Same as scenario 3, but add `auto_install=no` and remove `homepage`.

Check:
- [ ] The installer stays away even though no homepage is configured

## Scenario 5 — Domain whitelist

Config on `KIOSK_CFG`:

```ini
homepage=https://example.com
whitelist=example.com
```

Check:
- [ ] `example.com` loads
- [ ] Any other domain is blocked

This exercises the config fetcher end to end. It ran after the remote-config
step that used to abort the whole script, so a working whitelist is good
evidence that the rest of the config was applied too.

## Scenario 6 — Remote config

Serve a config file over HTTP from the Windows host, then boot with a
`KIOSK_CFG` config containing only:

```ini
kiosk_config=http://<host-ip>:8000/remote.conf
```

Check:
- [ ] The remote homepage is used, not the local one
- [ ] An `auto_install=yes` line in the **remote** file is ignored

The second check is a security property: a remote config must never be able to
trigger a disk wipe.

## Scenario 7 — Installer API cannot be driven by a website

The installer API listens on `127.0.0.1:8484` while the installer is on screen.
No website loaded in the kiosk browser may be able to reach it.

Automated coverage lives in `tests/test_installer_api.py` and runs in CI. To
confirm it by hand, boot the installer, press **Skip**, and verify:

- [ ] The browser leaves the installer and loads the configured homepage
- [ ] `http://127.0.0.1:8484/` is no longer reachable afterwards

The API shuts itself down on skip, so that the kiosk never browses the web with
a live install endpoint behind it.

## What to capture when something fails

Boot is silent by design (`quiet`, `splash`, `loglevel=0`), which makes a hang
hard to diagnose. If a scenario fails, capture:

- A screenshot of where it stopped
- Which scenario and which step
- Whether the disk was clean beforehand

A black screen after the splash usually means the compositor failed to start,
not that the system is dead.
