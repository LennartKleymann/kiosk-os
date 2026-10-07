#!/bin/bash
VM_DIR=${VM_DIR:-/root/kiosk-vm}
# Usage: run-vm.sh <name> [--stick img] [--disk qcow2] [--nonet]
# The stick is attached as USB mass storage (id usbstick, hot-removable via QMP).
set -euo pipefail
NAME=$1; shift
STICK=""; DISK=""; NET="-nic user,model=virtio-net-pci,hostfwd=tcp::${SSH_PORT:-2222}-${GUEST_IP:-}:22"
while [ $# -gt 0 ]; do case $1 in
  --stick) STICK=$2; shift 2;; --disk) DISK=$2; shift 2;; --nonet) NET="-nic none"; shift;; *) shift;; esac; done
mkdir -p $VM_DIR; cd $VM_DIR
[ -f vars-$NAME.fd ] || cp /usr/share/OVMF/OVMF_VARS_4M.fd vars-$NAME.fd
ARGS=(-name "kiosk-os test: $NAME" -machine q35,accel=kvm -cpu host -smp 4 -m 4096
  -drive if=pflash,format=raw,readonly=on,file=/usr/share/OVMF/OVMF_CODE_4M.fd
  -drive if=pflash,format=raw,file=vars-$NAME.fd
  ${VGA:--device virtio-vga} -display gtk,zoom-to-fit=off
  -device qemu-xhci,id=xhci -device usb-tablet -device usb-kbd
  -qmp unix:$VM_DIR/qmp.sock,server,nowait -serial file:$VM_DIR/serial-$NAME.log
  $NET)
[ -n "$DISK" ] && ARGS+=(-drive if=none,id=hd,file=$DISK -device nvme,drive=hd,serial=KIOSKTEST01)
# No bootindex: the firmware's own boot order decides, like on real hardware
[ -n "$STICK" ] && ARGS+=(-drive if=none,id=stick,format=raw,file=$STICK -device usb-storage,id=usbstick,drive=stick,removable=on)
exec qemu-system-x86_64 "${ARGS[@]}"
