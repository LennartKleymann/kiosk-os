#!/bin/bash
VM_DIR=${VM_DIR:-/root/kiosk-vm}
HOST=$(ip -4 -o addr show scope global | awk '$2!="lo"{print $4}' | cut -d/ -f1 | head -1)
ssh -q -i $VM_DIR/id_test -p ${SSH_PORT:-2222} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=5 admin@$HOST "$@"
