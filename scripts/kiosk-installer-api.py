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
STATE_FILE = "/tmp/kiosk-install-state.json"
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


def install(disk):
    """Install kiosk-os to the target disk."""
    p = partition_suffix(disk)
    part_efi = f"{disk}{p}1"
    part_root = f"{disk}{p}2"
    part_cfg = f"{disk}{p}3"

    # --- Step 1: Partition ---
    write_state("running", 5, f"Partitioning {disk}...", "partition", [])

    # Unmount anything on the target disk
    for i in range(1, 10):
        try_run(f"umount {disk}{p}{i}")
        try_run(f"umount {disk}{i}")
    for mp in [f"{TARGET}/boot", TARGET, "/mnt/kiosk-cfg-target"]:
        try_run(f"umount {mp}")

    run(f"wipefs -a {disk}")
    run(f"parted -s {disk} mklabel gpt")
    run(f"parted -s {disk} mkpart ESP fat32 1MiB 513MiB")
    run(f"parted -s {disk} set 1 esp on")
    run(f"parted -s {disk} mkpart primary ext4 513MiB 95%")
    run(f"parted -s {disk} mkpart primary fat32 95% 100%")
    try_run(f"partprobe {disk}")

    # Wait for partition device nodes to appear
    for part in (part_efi, part_root, part_cfg):
        if not wait_for_partition(part):
            raise RuntimeError(f"partition {part} did not appear after partitioning")

    # --- Step 2: Format ---
    write_state("running", 15, "Formatting partitions...", "format", ["partition"])

    run(f"mkfs.fat -F32 -n KIOSK_EFI {part_efi}")
    run(f"mkfs.ext4 -L KIOSK_ROOT -F {part_root}")
    run(f"mkfs.fat -F32 -n KIOSK_CFG {part_cfg}")

    # --- Step 3: Mount ---
    write_state("running", 20, "Mounting target...", "copy", ["partition", "format"])

    run(f"mkdir -p {TARGET}")
    run(f"mount {part_root} {TARGET}")
    run(f"mkdir -p {TARGET}/boot")
    run(f"mount {part_efi} {TARGET}/boot")

    # --- Step 4: Copy system ---
    write_state("running", 25, "Copying system (this may take a few minutes)...", "copy", ["partition", "format"])

    with open(DISK_SYSTEM_FILE) as f:
        disk_system = f.read().strip()
    run(f"nixos-install --root {TARGET} --no-root-passwd --system {disk_system}")

    # --- Step 5: Activate bootloader ---
    write_state("running", 80, "Installing bootloader...", "bootloader", ["partition", "format", "copy"])

    for d in ("dev", "proc", "sys"):
        run(f"mount --bind /{d} {TARGET}/{d}")

    # Ensure boot partition is mounted inside chroot
    try_run(f"umount {TARGET}/boot")
    run(f"mount {part_efi} {TARGET}/boot")

    run(f"NIXOS_INSTALL_BOOTLOADER=1 chroot {TARGET} /nix/var/nix/profiles/system/bin/switch-to-configuration boot")

    # --- Step 6: Copy kiosk config to config partition ---
    write_state("running", 90, "Writing configuration...", "config", ["partition", "format", "copy", "bootloader"])

    run("mkdir -p /mnt/kiosk-cfg-target")
    run(f"mount {part_cfg} /mnt/kiosk-cfg-target")
    if os.path.exists(CONFIG_FILE):
        run(f"cp {CONFIG_FILE} /mnt/kiosk-cfg-target/kiosk.conf")
    elif os.path.exists("/etc/kiosk/default.conf"):
        run("cp /etc/kiosk/default.conf /mnt/kiosk-cfg-target/kiosk.conf")
    try_run("umount /mnt/kiosk-cfg-target")

    # --- Step 7: Cleanup ---
    write_state("running", 95, "Finalizing...", "finalize", ["partition", "format", "copy", "bootloader", "config"])

    for d in ("sys", "proc", "dev"):
        try_run(f"umount {TARGET}/{d}")
    try_run(f"umount {TARGET}/boot")
    try_run(f"umount {TARGET}")
    run("sync")

    write_state("done", 100, "Installation complete", "", ["partition", "format", "copy", "bootloader", "config", "finalize"])


def run_install_thread(disk):
    """Wrapper that catches errors and writes them to state."""
    try:
        install(disk)
    except Exception as e:
        write_state("error", 0, f"Installation failed: {e}", "", [])


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

            # Strict validation: only allow /dev/<alphanumeric>
            if not VALID_DISK_PATH.match(disk) or not os.path.exists(disk):
                self.send_json({"error": "Invalid disk path"}, 400)
                return

            threading.Thread(target=run_install_thread, args=(disk,), daemon=True).start()
            self.send_json({"status": "started"})

        elif self.path == "/skip":
            homepage = read_config_value("homepage", "https://example.com")
            self.send_json({"status": "skipped", "homepage": homepage})
            # Shut down once the user opts out: the kiosk is about to load a
            # real website and this API must not outlive the installer UI.
            try:
                os.unlink(TOKEN_FILE)
            except OSError:
                pass
            threading.Timer(1.0, lambda: os._exit(0)).start()

        elif self.path == "/reboot":
            self.send_json({"status": "rebooting"})
            threading.Timer(2.0, lambda: subprocess.run(["reboot"], check=False)).start()

        else:
            self.send_json({"error": "not found"}, 404)


if __name__ == "__main__":
    write_state("idle", 0, "", "", [])
    server = HTTPServer((LISTEN_HOST, LISTEN_PORT), Handler)
    print(f"[kiosk-installer] API listening on http://{LISTEN_HOST}:{LISTEN_PORT}")
    server.serve_forever()
