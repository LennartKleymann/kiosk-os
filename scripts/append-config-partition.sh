#!/usr/bin/env bash
# Appends an empty FAT partition labelled KIOSK_CFG to a hybrid ISO image.
#
#   append-config-partition.sh <image> [size-in-MiB]
#
# The image is an isohybrid with a syslinux MBR, so the entry goes into a free
# MBR slot. sfdisk only rewrites bytes 446..510, leaving the boot code ahead of
# it untouched.
#
# xorriso also writes a GPT next to that MBR. Linux and UEFI firmware go by
# the MBR (it is not a protective one), but Windows does not: as long as the
# GPT is there it creates no volume for any partition on the stick, so the
# KIOSK_CFG partition never gets a drive letter and the setup wizard cannot
# write kiosk.conf. The GPT is therefore removed — headers and entry arrays,
# primary and backup. The MBR alone boots on UEFI and BIOS machines.
set -euo pipefail

image=${1:?usage: append-config-partition.sh <image> [size-in-MiB]}
size_mib=${2:-16}
align_mib=1

remove_gpt() {
  local img=$1 backup lba off entries count size
  [ "$(dd if="$img" bs=1 skip=512 count=8 status=none)" = "EFI PART" ] || return 0
  backup=$(od -An -t u8 -j $(( 512 + 32 )) -N 8 "$img" | tr -d ' ')
  for lba in 1 "$backup"; do
    off=$(( lba * 512 ))
    [ "$(dd if="$img" bs=1 skip="$off" count=8 status=none)" = "EFI PART" ] || continue
    entries=$(od -An -t u8 -j $(( off + 72 )) -N 8 "$img" | tr -d ' ')
    count=$(od -An -t u4 -j $(( off + 80 )) -N 4 "$img" | tr -d ' ')
    size=$(od -An -t u4 -j $(( off + 84 )) -N 4 "$img" | tr -d ' ')
    dd if=/dev/zero of="$img" bs=512 seek="$entries" count=$(( (count * size + 511) / 512 )) conv=notrunc status=none
    dd if=/dev/zero of="$img" bs=512 seek="$lba" count=1 conv=notrunc status=none
  done
  echo "removed GPT (primary and backup at sector $backup)"
}

remove_gpt "$image"

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
