#!/usr/bin/env python3
"""kiosk-os installer API — lightweight HTTP server for the browser-based installer."""

import json
import os
import subprocess
import threading
from http.server import HTTPServer, BaseHTTPRequestHandler

INSTALL_STATE = {"status": "idle", "percent": 0, "message": "", "current": "", "completed": []}
CONFIG_FILE = "/etc/kiosk/config"


def get_disks():
    """List available disks using lsblk."""
    try:
        result = subprocess.run(
            ["lsblk", "-J", "-o", "NAME,SIZE,MODEL,TYPE,TRAN,RM,PATH", "-d"],
            capture_output=True, text=True
        )
        data = json.loads(result.stdout)
        disks = []
        for d in data.get("blockdevices", []):
            if d.get("type") != "disk":
                continue
            if d.get("name", "").startswith("loop"):
                continue
            name = d.get("name", "")
            path = d.get("path") or f"/dev/{name}"
            model = (d.get("model") or "").strip()
            size = d.get("size") or "unknown"
            tran = d.get("tran") or ""

            disks.append({
                "path": path,
                "size": size,
                "model": model if model else path,
                "transport": tran,
                "removable": d.get("rm") in (True, "1", 1),
            })
        return disks
    except Exception as e:
        return [{"error": str(e)}]


def run_install(disk: str):
    """Run the installation process in a background thread."""
    global INSTALL_STATE

    def update(status, percent, message, current, completed):
        INSTALL_STATE = {
            "status": status, "percent": percent,
            "message": message, "current": current, "completed": completed,
        }
        # Also write to file so the handler can read it
        with open("/tmp/kiosk-install-state.json", "w") as f:
            json.dump(INSTALL_STATE, f)

    def run(cmd, **kwargs):
        subprocess.run(cmd, shell=True, check=True, capture_output=True, **kwargs)

    try:
        # Detect partition naming
        p = "p" if "nvme" in disk or "mmcblk" in disk else ""
        part_efi = f"{disk}{p}1"
        part_root = f"{disk}{p}2"
        part_config = f"{disk}{p}3"

        # Step 1: Partition
        update("running", 5, f"Partitioning {disk}...", "partition", [])
        run(f"wipefs -a {disk}")
        run(f"parted -s {disk} mklabel gpt")
        run(f"parted -s {disk} mkpart ESP fat32 1MiB 513MiB")
        run(f"parted -s {disk} set 1 esp on")
        run(f"parted -s {disk} mkpart primary ext4 513MiB 95%")
        run(f"parted -s {disk} mkpart primary fat32 95% 100%")
        run(f"partprobe {disk} || true")
        subprocess.run(["sleep", "2"])

        # Step 2: Format
        update("running", 20, "Formatting partitions...", "format", ["partition"])
        run(f"mkfs.fat -F32 -n KIOSK_EFI {part_efi}")
        run(f"mkfs.ext4 -L KIOSK_ROOT -F {part_root}")
        run(f"mkfs.fat -F32 -n KIOSK_CFG {part_config}")

        # Step 3: Copy system
        update("running", 35, "Copying system files (this may take a few minutes)...", "copy", ["partition", "format"])
        run("mkdir -p /mnt/kiosk-install")
        run(f"mount {part_root} /mnt/kiosk-install")
        run("mkdir -p /mnt/kiosk-install/boot")
        run(f"mount {part_efi} /mnt/kiosk-install/boot")
        run("nixos-install --root /mnt/kiosk-install --no-root-passwd --system /run/current-system")

        # Step 4: Bootloader
        update("running", 80, "Installing bootloader...", "bootloader", ["partition", "format", "copy"])
        run("bootctl install --path=/mnt/kiosk-install/boot || true")

        # Step 5: Config
        update("running", 90, "Writing configuration...", "config", ["partition", "format", "copy", "bootloader"])
        run("mkdir -p /mnt/kiosk-config")
        run(f"mount {part_config} /mnt/kiosk-config")
        if os.path.exists(CONFIG_FILE):
            run(f"cp {CONFIG_FILE} /mnt/kiosk-config/kiosk.conf")
        elif os.path.exists("/etc/kiosk/default.conf"):
            run("cp /etc/kiosk/default.conf /mnt/kiosk-config/kiosk.conf")
        run("umount /mnt/kiosk-config")

        # Step 6: Finalize
        update("running", 95, "Finalizing...", "finalize", ["partition", "format", "copy", "bootloader", "config"])
        run("umount /mnt/kiosk-install/boot || true")
        run("umount /mnt/kiosk-install || true")
        run("sync")

        update("done", 100, "Installation complete", "", ["partition", "format", "copy", "bootloader", "config", "finalize"])

    except subprocess.CalledProcessError as e:
        update("error", 0, f"Installation failed: {e.stderr.decode() if e.stderr else str(e)}", "", [])
    except Exception as e:
        update("error", 0, f"Installation failed: {str(e)}", "", [])


class InstallerHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass  # Suppress default logging

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
                with open("/tmp/kiosk-install-state.json") as f:
                    self.send_json(json.load(f))
            except FileNotFoundError:
                self.send_json(INSTALL_STATE)
        else:
            self.send_json({"error": "not found"}, 404)

    def do_POST(self):
        content_length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(content_length).decode() if content_length > 0 else ""

        if self.path == "/install":
            try:
                data = json.loads(body)
                disk = data.get("disk", "")
                if not disk or not os.path.exists(disk):
                    self.send_json({"error": "Invalid disk"}, 400)
                    return
                # Start installation in background
                thread = threading.Thread(target=run_install, args=(disk,), daemon=True)
                thread.start()
                self.send_json({"status": "started"})
            except (json.JSONDecodeError, KeyError):
                self.send_json({"error": "Invalid request"}, 400)

        elif self.path == "/skip":
            open("/tmp/kiosk-skip-install", "w").close()
            self.send_json({"status": "skipped"})

        elif self.path == "/reboot":
            self.send_json({"status": "rebooting"})
            threading.Timer(2.0, lambda: os.system("reboot")).start()

        else:
            self.send_json({"error": "not found"}, 404)


if __name__ == "__main__":
    server = HTTPServer(("127.0.0.1", 8484), InstallerHandler)
    print("[kiosk-installer] API listening on http://127.0.0.1:8484")
    server.serve_forever()
