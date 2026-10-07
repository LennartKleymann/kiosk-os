#!/usr/bin/env python3
"""kiosk-os installer API — HTTP server for the browser-based disk installer."""

import json
import os
import re
import stat
import subprocess
import threading
import time
from http.server import HTTPServer, BaseHTTPRequestHandler

CONFIG_FILE = "/etc/kiosk/config"
DISK_SYSTEM_FILE = "/etc/kiosk/disk-system"
INSTALLER_HTML = "/etc/kiosk/installer.html"
TOKEN_FILE = "/run/kiosk/installer-token"
OVERRIDE_FILE = "/run/kiosk/homepage-override"
STATE_FILE = "/run/kiosk/install-state.json"
TARGET = "/mnt"
LISTEN_HOST = "127.0.0.1"
LISTEN_PORT = 8484
VALID_DISK_PATH = re.compile(r"^/dev/[a-zA-Z0-9]+$")
ALLOWED_ORIGINS = {f"http://{LISTEN_HOST}:{LISTEN_PORT}"}

# NixOS stores binaries in /run/current-system/sw/bin
os.environ["PATH"] = "/run/current-system/sw/bin:" + os.environ.get("PATH", "")

_state_lock = threading.Lock()


def write_state(status, percent, message, current, completed):
    """Write installation progress to a JSON file for the frontend to poll."""
    with _state_lock:
        tmp = f"{STATE_FILE}.tmp"
        with open(tmp, "w") as f:
            json.dump({
                "status": status,
                "percent": percent,
                "message": message,
                "current": current,
                "completed": completed,
            }, f)
        os.chmod(tmp, stat.S_IRUSR | stat.S_IWUSR)  # 0600
        os.replace(tmp, STATE_FILE)


def run(cmd):
    """Run a shell command, raise on failure."""
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"{cmd}: {result.stderr.strip()}")
    return result.stdout.strip()


def try_run(cmd):
    """Run a shell command, ignore failures."""
    subprocess.run(cmd, shell=True, capture_output=True)


def wait_for_partition(part_path, timeout=10):
    """Wait for a partition device node to appear in /dev."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        if os.path.exists(part_path):
            return True
        time.sleep(0.2)
    return False


def read_token():
    """Read the one-time token written by the installer gate."""
    try:
        with open(TOKEN_FILE) as f:
            return f.read().strip()
    except OSError:
        return ""


def boot_disks():
    """Parent disks of the live boot medium — never valid install targets."""
    found = set()
    for source in ("/iso", "/nix/.ro-store", "/"):
        try:
            src = subprocess.run(
                ["findmnt", "-n", "-o", "SOURCE", "--target", source],
                capture_output=True, text=True,
            ).stdout.strip()
        except Exception:
            continue
        if not src.startswith("/dev/"):
            continue
        try:
            parent = subprocess.run(
                ["lsblk", "-n", "-o", "PKNAME", src],
                capture_output=True, text=True,
            ).stdout.strip().split("\n")[0].strip()
        except Exception:
            parent = ""
        found.add(f"/dev/{parent}" if parent else src)
    return found


def get_disks():
    """List available disks via lsblk, excluding the live boot medium."""
    try:
        excluded = boot_disks()
        result = subprocess.run(
            ["lsblk", "-J", "-o", "NAME,SIZE,MODEL,TYPE,TRAN,RM,PATH", "-d"],
            capture_output=True, text=True,
        )
        disks = []
        for d in json.loads(result.stdout).get("blockdevices", []):
            if d.get("type") != "disk" or d.get("name", "").startswith("loop"):
                continue
            path = d.get("path") or f"/dev/{d.get('name', '')}"
            if path in excluded:
                continue
            model = (d.get("model") or "").strip()
            disks.append({
                "path": path,
                "size": d.get("size") or "unknown",
                "model": model or path,
                "transport": d.get("tran") or "",
                "removable": d.get("rm") in (True, "1", 1),
            })
        return disks
    except Exception as e:
        return [{"error": str(e)}]


def read_config_value(key, default=""):
    """Read a single value from the kiosk config file."""
    if not os.path.exists(CONFIG_FILE):
        return default
    with open(CONFIG_FILE) as f:
        for line in f:
            line = line.strip()
            if line.startswith(f"{key}="):
                return line.split("=", 1)[1]
    return default


def partition_suffix(disk):
    """NVMe and MMC disks use 'p' before partition numbers."""
    return "p" if "nvme" in disk or "mmcblk" in disk else ""


STEPS = ["partition", "format", "copy", "bootloader", "config", "finalize"]
LOCAL_CONFIG = "/run/kiosk/local.conf"
INSTALL_LOG = "/run/kiosk/nixos-install.log"
EFI_MIB = 512
CFG_MIB = 256

install_lock = threading.Lock()
install_running = False


def progress(percent, message, current):
    write_state("running", percent, message, current, STEPS[:STEPS.index(current)])


def human_size(n):
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if n < 1000 or unit == "TB":
            return f"{n:.1f} {unit}"
        n /= 1000


def watch_copy(stop, closure_bytes):
    """Report copy progress as used space on the target vs. the system size."""
    while not stop.is_set():
        try:
            st = os.statvfs(TARGET)
            used = (st.f_blocks - st.f_bfree) * st.f_frsize
            frac = min(used / closure_bytes, 1.0) if closure_bytes else 0
            # Used space includes file system overhead and can exceed the
            # system size near the end
            shown = min(used, closure_bytes) if closure_bytes else used
            progress(25 + int(frac * 50),
                     f"Copying system files... {human_size(shown)} of {human_size(closure_bytes)}", "copy")
        except OSError:
            pass
        stop.wait(2)


def add_efi_boot_entry(disk):
    """Put the installed disk first in the firmware boot order.

    Without this, a machine that still has the stick plugged in boots the
    installer again. Failure is not fatal: GRUB is installed to the removable
    fallback path, which firmware finds on its own once the stick is gone.
    """
    if not os.path.isdir("/sys/firmware/efi"):
        return
    result = subprocess.run(
        ["efibootmgr", "--create", "--disk", disk, "--part", "1",
         "--label", "kiosk-os", "--loader", "\\EFI\\BOOT\\BOOTX64.EFI"],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        print(f"[kiosk-installer] efibootmgr failed: {result.stderr.strip()}", flush=True)


def install(disk):
    """Install kiosk-os to the target disk."""
    boot_devices = boot_disks()
    p = partition_suffix(disk)
    part_efi = f"{disk}{p}1"
    part_root = f"{disk}{p}2"
    part_cfg = f"{disk}{p}3"

    # --- Step 1: Partition: 512 MiB EFI | root | 256 MiB config ---
    progress(5, f"Partitioning {disk}...", "partition")

    for mp in [f"{TARGET}/boot", TARGET, "/mnt/kiosk-cfg-target"]:
        try_run(f"umount -R {mp}")
    for part in run(f"lsblk -lnpo NAME {disk}").splitlines()[1:]:
        try_run(f"umount {part.strip()}")
        try_run(f"wipefs -a {part.strip()}")

    size_mib = int(run(f"blockdev --getsize64 {disk}")) // (1024 * 1024)
    cfg_start = size_mib - CFG_MIB - 1

    run(f"wipefs -a {disk}")
    run(f"parted -s {disk} mklabel gpt")
    run(f"parted -s {disk} mkpart KIOSK_EFI fat32 1MiB {EFI_MIB + 1}MiB")
    run(f"parted -s {disk} set 1 esp on")
    run(f"parted -s {disk} mkpart KIOSK_ROOT ext4 {EFI_MIB + 1}MiB {cfg_start}MiB")
    run(f"parted -s {disk} mkpart KIOSK_CFG fat32 {cfg_start}MiB {size_mib - 1}MiB")
    try_run(f"partprobe {disk}")
    try_run("udevadm settle")

    # Wait for partition device nodes to appear
    for part in (part_efi, part_root, part_cfg):
        if not wait_for_partition(part):
            raise RuntimeError(f"partition {part} did not appear after partitioning")

    # --- Step 2: Format ---
    progress(15, "Formatting partitions...", "format")

    run(f"mkfs.fat -F32 -n KIOSK_EFI {part_efi}")
    run(f"mkfs.ext4 -q -L KIOSK_ROOT -F {part_root}")
    run(f"mkfs.fat -F32 -n KIOSK_CFG {part_cfg}")
    try_run("udevadm settle")

    # --- Step 3: Copy system; nixos-install also installs the bootloader ---
    progress(20, "Mounting target...", "copy")

    run(f"mkdir -p {TARGET}")
    run(f"mount {part_root} {TARGET}")
    run(f"mkdir -p {TARGET}/boot")
    run(f"mount {part_efi} {TARGET}/boot")

    with open(DISK_SYSTEM_FILE) as f:
        disk_system = f.read().strip()
    try:
        closure = int(run(
            f"nix --extra-experimental-features nix-command path-info -S {disk_system}").split()[-1])
    except (RuntimeError, ValueError, IndexError):
        closure = 0

    stop = threading.Event()
    threading.Thread(target=watch_copy, args=(stop, closure), daemon=True).start()
    proc = subprocess.Popen(
        ["nixos-install", "--root", TARGET, "--system", disk_system,
         "--no-root-passwd", "--no-channel-copy"],
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
    )
    log = []
    for line in proc.stdout:
        log.append(line.rstrip())
        if "installing the boot loader" in line:
            stop.set()
            progress(80, "Installing bootloader...", "bootloader")
    proc.wait()
    stop.set()
    with open(INSTALL_LOG, "w") as f:
        f.write("\n".join(log))
    if proc.returncode != 0:
        raise RuntimeError("nixos-install failed: " + " | ".join(log[-3:]))
    if not os.path.exists(f"{TARGET}/boot/EFI/BOOT/BOOTX64.EFI"):
        raise RuntimeError("bootloader missing on the EFI partition after nixos-install")

    # --- Step 4: Copy the kiosk config to the config partition ---
    progress(90, "Writing configuration...", "config")

    run("mkdir -p /mnt/kiosk-cfg-target")
    run(f"mount {part_cfg} /mnt/kiosk-cfg-target")
    source = next((c for c in (LOCAL_CONFIG, CONFIG_FILE, "/etc/kiosk/default.conf")
                   if os.path.exists(c)), None)
    if source:
        with open(source) as f:
            # auto_install only means something on the stick
            lines = [l for l in f.read().splitlines()
                     if not re.match(r"^\s*auto_install\s*=", l)]
        with open("/mnt/kiosk-cfg-target/kiosk.conf", "w") as f:
            f.write("\n".join(lines) + "\n")
    try_run("umount /mnt/kiosk-cfg-target")

    # --- Step 5: Cleanup ---
    progress(95, "Finalizing...", "finalize")

    try_run(f"umount -R {TARGET}")
    add_efi_boot_entry(disk)
    run("sync")

    write_state("done", 100, "Installation complete", "", list(STEPS))
    reboot_when_stick_is_pulled(boot_devices)


def run_install_thread(disk):
    """Wrapper that catches errors and writes them to state."""
    global install_running
    try:
        install(disk)
    except Exception as e:
        try_run(f"umount -R {TARGET}")
        try_run("umount /mnt/kiosk-cfg-target")
        write_state("error", 0, f"Installation failed: {e}", "", [])
    finally:
        with install_lock:
            install_running = False


def hard_reboot():
    """Reboot without touching the boot medium.

    The live system runs from the stick. Once it is pulled, every binary on it
    is gone — systemctl and reboot included — so the reboot goes straight
    through the kernel. Nothing on a live system needs to be saved.
    """
    os.sync()
    try:
        with open("/proc/sysrq-trigger", "w") as f:
            f.write("b")
    except OSError:
        subprocess.run(["reboot", "-f"], check=False)


def reboot_when_stick_is_pulled(boot_devices):
    """After a successful install, restart as soon as the stick is removed.

    Pulling the stick takes the browser down with it (it runs from the stick),
    so a click on "Reboot now" afterwards would never arrive. This process is
    locked in RAM and only watches sysfs, so it keeps working.
    """
    # The squashfs loop device never goes away; only physical media count
    names = [os.path.basename(d) for d in boot_devices
             if not os.path.basename(d).startswith(("loop", "zram", "ram"))]
    if not names:
        return

    def watch():
        while all(os.path.exists(f"/sys/block/{n}") for n in names):
            time.sleep(0.5)
        print("[kiosk-installer] Boot medium removed, rebooting", flush=True)
        hard_reboot()

    threading.Thread(target=watch, daemon=True).start()


def leave_installer():
    """Start the regular kiosk session and stop this API.

    Restarting the session (rather than letting the page navigate away) runs
    the normal start-up path: reachability check, offline error page with
    auto-retry, wallpaper.
    """
    try:
        subprocess.run(["systemctl", "restart", "--no-block", "cage-tty1.service"],
                       capture_output=True, timeout=10)
    except (OSError, subprocess.SubprocessError):
        pass
    os._exit(0)


def lock_in_memory():
    """Keep this process in RAM so it still works after the stick is pulled."""
    try:
        import ctypes
        ctypes.CDLL(None, use_errno=True).mlockall(1 | 2)  # MCL_CURRENT | MCL_FUTURE
    except Exception:
        pass


# --- HTTP API ---

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass  # Silence default access log

    def send_json(self, data, status=200):
        body = json.dumps(data).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", len(body))
        self.end_headers()
        self.wfile.write(body)

    def send_installer_page(self):
        """Serve the installer UI with the current token embedded."""
        try:
            with open(INSTALLER_HTML) as f:
                page = f.read().replace("__KIOSK_TOKEN__", read_token())
        except OSError as e:
            self.send_json({"error": f"installer UI missing: {e}"}, 500)
            return
        body = page.encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", len(body))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def request_is_authorized(self):
        """Reject anything that is not the locally served installer page.

        Without this, any site loaded in the kiosk browser could POST to this
        API and wipe the disk — a form or fetch() needs no preflight.
        """
        origin = self.headers.get("Origin")
        if origin is not None and origin not in ALLOWED_ORIGINS:
            return False
        token = read_token()
        return bool(token) and self.headers.get("X-Kiosk-Token", "") == token

    def do_GET(self):
        if self.path in ("/", "/index.html"):
            self.send_installer_page()
        elif self.path == "/health":
            self.send_json({"status": "ok"})
        elif self.path == "/disks":
            self.send_json(get_disks())
        elif self.path == "/progress":
            try:
                with open(STATE_FILE) as f:
                    self.send_json(json.load(f))
            except FileNotFoundError:
                self.send_json({"status": "idle", "percent": 0, "message": "", "current": "", "completed": []})
        else:
            self.send_json({"error": "not found"}, 404)

    def do_POST(self):
        if not self.request_is_authorized():
            self.send_json({"error": "Forbidden"}, 403)
            return

        try:
            length = int(self.headers.get("Content-Length", 0))
        except (TypeError, ValueError):
            self.send_json({"error": "Invalid Content-Length"}, 400)
            return

        body = self.rfile.read(length).decode() if length > 0 else ""

        if self.path == "/install":
            try:
                disk = json.loads(body).get("disk", "")
            except json.JSONDecodeError:
                self.send_json({"error": "Invalid JSON"}, 400)
                return

            # Strict validation: only allow /dev/<alphanumeric>, and only a
            # disk the UI offered — never the boot medium
            if not VALID_DISK_PATH.match(disk) or not os.path.exists(disk):
                self.send_json({"error": "Invalid disk path"}, 400)
                return
            if disk not in [d.get("path") for d in get_disks()]:
                self.send_json({"error": "Disk is not an install target"}, 400)
                return

            global install_running
            with install_lock:
                if install_running:
                    self.send_json({"error": "Installation already running"}, 409)
                    return
                install_running = True
            progress(1, "Starting installation...", "partition")
            threading.Thread(target=run_install_thread, args=(disk,), daemon=True).start()
            self.send_json({"status": "started"})

        elif self.path == "/skip":
            homepage = read_config_value("homepage", "https://example.com")
            self.send_json({"status": "skipped", "homepage": homepage})
            # Shut down once the user opts out: the kiosk is about to load a
            # real website and this API must not outlive the installer UI.
            for path in (TOKEN_FILE, OVERRIDE_FILE):
                try:
                    os.unlink(path)
                except OSError:
                    pass
            threading.Timer(1.0, leave_installer).start()

        elif self.path == "/reboot":
            self.send_json({"status": "rebooting"})
            threading.Timer(1.0, hard_reboot).start()

        else:
            self.send_json({"error": "not found"}, 404)


if __name__ == "__main__":
    lock_in_memory()
    write_state("idle", 0, "", "", [])
    server = HTTPServer((LISTEN_HOST, LISTEN_PORT), Handler)
    print(f"[kiosk-installer] API listening on http://{LISTEN_HOST}:{LISTEN_PORT}")
    server.serve_forever()
