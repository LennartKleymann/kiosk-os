#!/bin/bash
# Real WPA2 association test: hwsim radios + hostapd AP inside the kiosk VM.
HERE=$(cd "$(dirname "$0")" && pwd)
VM_DIR=${VM_DIR:-/root/kiosk-vm}
# The AP radio lives in its own network namespace, so the kiosk's
# wpa_supplicant only sees "its" WiFi card — like on real hardware.
set -e
. /nix/var/nix/profiles/default/etc/profile.d/nix-daemon.sh
cd "$HERE/../.."
H=$(nix build --no-link --print-out-paths .#nixosConfigurations.kiosk.pkgs.hostapd^out)
I=$(nix build --no-link --print-out-paths .#nixosConfigurations.kiosk.pkgs.iw^out)
nix-store --export $(nix-store -qR $H $I) > $VM_DIR/wifitools.closure
HOST=$(ip -4 -o addr show scope global | awk '$2!="lo"{print $4}' | cut -d/ -f1 | head -1)
scp -q -i $VM_DIR/id_test -P 2222 -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null $VM_DIR/wifitools.closure admin@$HOST:/tmp/
"$HERE/ssh.sh" "set -e
sudo nix-store --import < /tmp/wifitools.closure >/dev/null
lsmod | grep -q mac80211_hwsim || sudo modprobe mac80211_hwsim radios=2
sleep 1
sudo systemctl stop wpa_supplicant
sudo ip netns add apns 2>/dev/null || true
PHY=\$(cat /sys/class/net/wlan1/phy80211/name)
sudo $I/bin/iw phy \$PHY set netns name apns
printf 'interface=wlan1\ndriver=nl80211\nssid=KioskTestNet\nhw_mode=g\nchannel=6\nwpa=2\nwpa_key_mgmt=WPA-PSK\nrsn_pairwise=CCMP\nwpa_passphrase=kioskpass123\n' > /tmp/hostapd.conf
sudo ip netns exec apns $H/bin/hostapd -B /tmp/hostapd.conf >/dev/null && echo 'AP KioskTestNet up (separate namespace)'
echo 'kiosk sees:' \$(ls /sys/class/net)
# Exactly what happens at boot on a kiosk with a WiFi card:
sudo systemctl restart kiosk-config-fetcher
for i in \$(seq 1 20); do sudo wpa_cli -i wlan0 status | grep -q wpa_state=COMPLETED && break; sleep 1; done
sudo wpa_cli -i wlan0 status | grep -E 'wpa_state|^ssid|key_mgmt'"
