# VM end-to-end tests

Scripted counterpart to the manual plan in [docs/testing.md](../../docs/testing.md). Runs
kiosk-os in QEMU/KVM with UEFI firmware, attaches the "USB stick" as a real USB mass storage
device, and drives the kiosk with clicks and screenshots over QMP.

The stick is written by the **setup wizard's own code** (`tests/wizard-e2e`, a headless driver for
the same view model, `KioskConfig`, `FlashService` and `ConfigWriterService` the GUI uses), so a
test covers the whole chain: wizard → stick → live boot → installer → installed system.

## Requirements

Linux (WSL2 works) with KVM, run as root:

```bash
apt install qemu-system-x86 ovmf parted dosfstools xorriso python3 openssh-client
nix build .#iso                                   # in the repo root
dotnet publish tests/wizard-e2e -c Release -r linux-x64 --self-contained -o "$VM_DIR/wizard-e2e"
ssh-keygen -t ed25519 -N "" -f "$VM_DIR/id_test"
```

`VM_DIR` defaults to `/root/kiosk-vm`.

## Scripts

| Script | Purpose |
|---|---|
| `wizard-stick.sh <img> [extra] -- <wizard args>` | Writes a stick image with the wizard code. `extra` lines are appended with CRLF, like Notepad would |
| `run-vm.sh <name> [--stick img] [--disk qcow2]` | Boots the VM (UEFI, stick as USB device id `usbstick`, target disk as NVMe). `VGA="-vga std"` selects the plain VGA adapter, `GUEST_IP=` the forwarded address for static-IP tests, `SSH_PORT=` the host port for SSH (default 2222) |
| `qmp.py shot <png> \| click x y \| type text \| key combo \| raw cmd json` | Screenshot / mouse / keyboard / raw QMP. `raw device_del '{"id":"usbstick"}'` pulls the stick |
| `ssh.sh <cmd>` | Runs a command on the kiosk (needs `admin_ssh=yes` with the test key in the config) |
| `wifi-test.sh` | Real WPA2 association test: two `mac80211_hwsim` radios, a hostapd access point in its own network namespace, and the kiosk's own config fetcher connecting to it |

## Scenarios covered (release checklist)

1. Wizard stick with WiFi preset and *install to disk* → live boot applies every setting → kiosk
   associates with the WPA2 test AP → install by clicking → **stick stays in**, *Reboot now* →
   boots the internal disk (UEFI entry), config from the disk's `KIOSK_CFG`, WiFi preset carried over
2. Same, but **the stick is pulled** after the install → automatic reboot into the installed system
3. Live stick, `browser_mode=fullscreen`: toolbar visible; `dpms_idle` turns the display off,
   input turns it on; `session_idle` resets only a session that was used
4. Whitelist blocks other domains; a foreign USB stick plugged in later is de-authorized
5. *Skip* in the installer: API shuts down, normal session starts; unreachable homepage shows the
   offline page, which switches to the homepage once it answers
6. `dhcp=no` static IP + HTTPS remote config: network comes up before the fetch, remote keys
   override local ones, a remote `auto_install=yes` is ignored

Known emulator artefact: with QEMU's `virtio-vga` the screen sometimes stops repainting
(partial frames) while the system keeps working. With `VGA="-vga std"` it does not happen, so use
that for screenshots.
