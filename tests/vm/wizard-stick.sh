#!/bin/bash
VM_DIR=${VM_DIR:-/root/kiosk-vm}
# Creates a "USB stick" image with the real wizard code (headless harness).
#   wizard-stick.sh <stick.img> [extra-crlf-lines-file] -- <wizard args...>
# Extra lines are appended with CRLF endings, the way Notepad would save them.
set -euo pipefail
IMG=$1; shift
EXTRA=""
if [ "${1:-}" != "--" ]; then EXTRA=$1; shift; fi
shift  # --
ISO=$(readlink -f $(dirname "$0")/../../result)/iso/kiosk-os.iso
WIZ=$VM_DIR/wizard-e2e/WizardE2E

rm -f "$IMG"; truncate -s 4G "$IMG"
LOOP=$(losetup -fP --show "$IMG")
trap 'umount /tmp/kiosk-os-wizard-cfg 2>/dev/null || true; losetup -d "$LOOP" 2>/dev/null || true' EXIT

"$WIZ" --iso "$ISO" --device "$LOOP" "$@"

if [ -n "$EXTRA" ]; then
  umount /tmp/kiosk-os-wizard-cfg 2>/dev/null || true
  mkdir -p /tmp/cfgedit
  mount "$(lsblk -lnpo NAME,LABEL "$LOOP" | awk '$2=="KIOSK_CFG"{print $1}')" /tmp/cfgedit
  sed 's/$/\r/' "$EXTRA" >> /tmp/cfgedit/kiosk.conf
  sync; umount /tmp/cfgedit
fi
echo "stick ready: $IMG"
