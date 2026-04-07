#!/usr/bin/env python3
"""kiosk-os installer API — HTTP server for the browser-based disk installer."""

import json
import os
import subprocess
import threading
from http.server import HTTPServer, BaseHTTPRequestHandler

CONFIG_FILE = "/etc/kiosk/config"
DISK_SYSTEM_FILE = "/etc/kiosk/disk-system"
STATE_FILE = "/tmp/kiosk-install-state.json"
TARGET = "/mnt"

# NixOS stores binaries in /run/current-system/sw/bin
os.environ["PATH"] = "/run/current-system/sw/bin:" + os.environ.get("PATH", "")


def write_state(status, percent, message, current, completed):
    """Write installation progress to a JSON file for the frontend to poll."""
    with open(STATE_FILE, "w") as f:
        json.dump({
            "status": status,
            "percent": percent,
            "message": message,
            "current": current,
            "completed": completed,
        }, f)


def run(cmd):
    """Run a shell command, raise on failure."""
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"{cmd}: {result.stderr.strip()}")
    return result.stdout.strip()


def try_run(cmd):
    """Run a shell command, ignore failures."""
    subprocess.run(cmd, shell=True, capture_output=True)


def get_disks():
    """List available disks via lsblk."""
    try:
        result = subprocess.run(
            ["lsblk", "-J", "-o", "NAME,SIZE,MODEL,TYPE,TRAN,RM,PATH", "-d"],
            capture_output=True, text=True,
        )
        disks = []
        for d in json.loads(result.stdout).get("blockdevices", []):
            if d.get("type") != "disk" or d.get("name", "").startswith("loop"):
                continue
            path = d.get("path") or f"/dev/{d.get('name', '')}"
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
    for mp in [f"{TARGET}/boot", TARGET, "/mnt/kiosk-config", "/mnt/kiosk-cfg"]:
        try_run(f"umount {mp}")

    run(f"wipefs -a {disk}")
    run(f"parted -s {disk} mklabel gpt")
    run(f"parted -s {disk} mkpart ESP fat32 1MiB 513MiB")
    run(f"parted -s {disk} set 1 esp on")
    run(f"parted -s {disk} mkpart primary ext4 513MiB 95%")
    run(f"parted -s {disk} mkpart primary fat32 95% 100%")
    try_run(f"partprobe {disk}")
    subprocess.run(["sleep", "2"])

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

    disk_system = open(DISK_SYSTEM_FILE).read().strip()
    run(f"nixos-install --root {TARGET} --no-root-passwd --system {disk_system}")

    # --- Step 5: Activate bootloader ---
    write_state("running", 80, "Installing bootloader...", "bootloader", ["partition", "format", "copy"])

    # Bind-mount system directories for chroot
    for d in ["dev", "proc", "sys"]:
        run(f"mount --bind /{d} {TARGET}/{d}")

    # Ensure boot partition is mounted inside chroot
    try_run(f"umount {TARGET}/boot")
    run(f"mount {part_efi} {TARGET}/boot")

    # Activate the system and install GRUB
    run(f"NIXOS_INSTALL_BOOTLOADER=1 chroot {TARGET} /nix/var/nix/profiles/system/bin/switch-to-configuration boot")

    # --- Step 6: Copy kiosk config ---
    write_state("running", 90, "Writing configuration...", "config", ["partition", "format", "copy", "bootloader"])

    run(f"mkdir -p /mnt/kiosk-cfg-target")
    run(f"mount {part_cfg} /mnt/kiosk-cfg-target")
    if os.path.exists(CONFIG_FILE):
        run(f"cp {CONFIG_FILE} /mnt/kiosk-cfg-target/kiosk.conf")
    elif os.path.exists("/etc/kiosk/default.conf"):
        run(f"cp /etc/kiosk/default.conf /mnt/kiosk-cfg-target/kiosk.conf")
    try_run("umount /mnt/kiosk-cfg-target")

    # --- Step 7: Cleanup ---
    write_state("running", 95, "Finalizing...", "finalize", ["partition", "format", "copy", "bootloader", "config"])

    for d in ["sys", "proc", "dev"]:
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
        pass

    def send_json(self, data, status=200):
        body = json.dumps(data).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", len(body))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_json({})

    def do_GET(self):
        if self.path == "/disks":
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
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length).decode() if length > 0 else ""

        if self.path == "/install":
            try:
                disk = json.loads(body).get("disk", "")
                if not disk or not os.path.exists(disk):
                    self.send_json({"error": "Invalid disk"}, 400)
                    return
                threading.Thread(target=run_install_thread, args=(disk,), daemon=True).start()
                self.send_json({"status": "started"})
            except (json.JSONDecodeError, KeyError):
                self.send_json({"error": "Invalid request"}, 400)

        elif self.path == "/skip":
            homepage = "https://example.com"
            if os.path.exists(CONFIG_FILE):
                for line in open(CONFIG_FILE):
                    if line.strip().startswith("homepage="):
                        homepage = line.strip().split("=", 1)[1]
                        break
            self.send_json({"status": "skipped", "homepage": homepage})

        elif self.path == "/reboot":
            self.send_json({"status": "rebooting"})
            threading.Timer(2.0, lambda: os.system("reboot")).start()

        else:
            self.send_json({"error": "not found"}, 404)


if __name__ == "__main__":
    write_state("idle", 0, "", "", [])
    server = HTTPServer(("127.0.0.1", 8484), Handler)
    print("[kiosk-installer] API listening on http://127.0.0.1:8484")
    server.serve_forever()
