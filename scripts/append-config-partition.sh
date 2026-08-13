#!/usr/bin/env bash
# Appends an empty FAT partition labelled KIOSK_CFG to a hybrid ISO image.
#
#   append-config-partition.sh <image> [size-in-MiB]
#
# The image is an isohybrid with a syslinux MBR and no GPT, so the entry goes
# into a free MBR slot. sfdisk only rewrites bytes 446..510, leaving the boot
# code ahead of it untouched.
set -euo pipefail

image=${1:?usage: append-config-partition.sh <image> [size-in-MiB]}
size_mib=${2:-16}
align_mib=1

sectors=$(( size_mib * 1024 * 1024 / 512 ))
align=$(( align_mib * 1024 * 1024 / 512 ))

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
cfg="$work/cfg.img"

# -C makes mkfs.fat create the file at the given size (in 1K blocks). Pointed
# at a pre-made empty file it cannot work out a geometry and bails out with
# "Invalid partition data!".
mkfs.vfat -F 16 -n KIOSK_CFG -C "$cfg" $(( size_mib * 1024 )) >/dev/null

truncate -s +$(( (size_mib + align_mib * 2) * 1024 * 1024 )) "$image"

last_end=$(sfdisk --json "$image" | jq '[.partitiontable.partitions[] | .start + .size] | max')
start=$(( (last_end + align - 1) / align * align ))

echo "$start,$sectors,0xe" | sfdisk --append --no-reread --no-tell-kernel "$image" >/dev/null

dd if="$cfg" of="$image" bs=512 seek="$start" conv=notrunc status=none

# The label is what the kiosk mounts by, so a silent failure here would only
# surface later as a kiosk ignoring its configuration.
found=$(blkid -p -o value -s LABEL -O $(( start * 512 )) "$image" || true)
if [ "$found" != "KIOSK_CFG" ]; then
  echo "expected label KIOSK_CFG at sector $start, got: $found" >&2
  exit 1
fi

echo "config partition at sector $start ($size_mib MiB)"
