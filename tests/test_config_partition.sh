#!/usr/bin/env bash
# Exercises append-config-partition.sh against a synthetic hybrid image so the
# command chain can be checked in seconds instead of rebuilding a 1.7 GB ISO.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
script="$here/../scripts/append-config-partition.sh"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
image="$work/test.iso"

# Stand-in for the isohybrid layout xorriso produces: a GPT (primary at LBA 1,
# backup at the end) plus a non-protective MBR with one occupied slot. An
# earlier version of this test had no GPT — which is exactly why it missed
# that Windows refuses to mount anything on such a stick.
truncate -s 64M "$image"
echo 'label: gpt' | sfdisk "$image" >/dev/null 2>&1
echo '2048,32768' | sfdisk --append --no-reread --no-tell-kernel "$image" >/dev/null 2>&1
# overwrite the protective MBR entry with an isohybrid-style entry
printf '\x80\x00\x00\x00\x17\x00\x00\x00\x00\x08\x00\x00\x00\x80\x00\x00' |
  dd of="$image" bs=1 seek=446 conv=notrunc status=none
[ "$(dd if="$image" bs=1 skip=512 count=8 status=none)" = "EFI PART" ] || { echo "test setup: no GPT"; exit 1; }

before=$(dd if="$image" bs=446 count=1 status=none | sha256sum)

bash "$script" "$image" 16

fail=0
check() {
  if [ "$2" = "$3" ]; then
    echo "PASS  $1"
  else
    echo "FAIL  $1: got '$2', want '$3'"
    fail=1
  fi
}

after=$(dd if="$image" bs=446 count=1 status=none | sha256sum)
check "boot code is untouched" "$after" "$before"

count=$(sfdisk --json "$image" | jq '.partitiontable.partitions | length')
check "second partition was added" "$count" "2"

original=$(sfdisk --json "$image" | jq -r '.partitiontable.partitions[0] | "\(.start),\(.size)"')
check "original partition is unchanged" "$original" "2048,32768"

start=$(sfdisk --json "$image" | jq -r '.partitiontable.partitions[1].start')
label=$(blkid -p -o value -s LABEL -O $(( start * 512 )) "$image")
check "config partition carries the label" "$label" "KIOSK_CFG"

fstype=$(blkid -p -o value -s TYPE -O $(( start * 512 )) "$image")
check "config partition is FAT" "$fstype" "vfat"

check "start is 1 MiB aligned" "$(( start % 2048 ))" "0"

# Windows creates no volumes on a disk that still carries GPT structures
check "primary GPT header removed" "$(dd if="$image" bs=1 skip=512 count=8 status=none | tr -d '\0')" ""
check "GPT entry array removed" "$(dd if="$image" bs=512 skip=2 count=32 status=none | tr -d '\0' | wc -c)" "0"
backup_hits=$(python3 -c "
import sys; d = open(sys.argv[1], 'rb').read()
print(sum(1 for i in range(0, len(d), 512) if d[i:i + 8] == b'EFI PART'))" "$image")
check "no GPT header anywhere (incl. backup)" "$backup_hits" "0"
check "partition table is read as MBR" "$(blkid -p -o value -s PTTYPE "$image")" "dos"

# The kiosk mounts this partition and reads kiosk.conf off it; if it cannot be
# mounted as a plain FAT filesystem, none of the configuration ever arrives.
mkdir -p "$work/mnt"
if command -v mcopy >/dev/null 2>&1; then
  printf 'homepage=https://example.com\n' > "$work/kiosk.conf"
  mcopy -i "$image"@@$(( start * 512 )) "$work/kiosk.conf" ::kiosk.conf
  mcopy -i "$image"@@$(( start * 512 )) ::kiosk.conf "$work/readback.conf"
  check "config survives a write and read back" \
    "$(cat "$work/readback.conf")" "homepage=https://example.com"
else
  echo "SKIP  mtools not available, filesystem write not exercised"
fi

echo
[ "$fail" -eq 0 ] && echo "all checks passed" || echo "checks failed"
exit "$fail"
