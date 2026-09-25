#!/usr/bin/env bash
# Enable RDP in the offline SYSTEM hive on the base image.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"
SYSTEM_HIVE="${MNT_P2}/Windows/System32/config/SYSTEM"

cleanup() {
  rm -f /tmp/tortoise-system.hive /tmp/tortoise-software.hive
  sudo umount "${MNT_P2}" 2>/dev/null || true
  sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
}
trap cleanup EXIT

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }
pgrep -f 'qemu-system-x86_64' >/dev/null && {
  echo "Refusing offline RDP inject while QEMU is running" >&2
  exit 1
}

sudo modprobe nbd max_part=8
sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
sudo qemu-nbd --connect="${NBD}" "${BASE}"
sleep 2
sudo ntfsfix -d "${NBD}p2" >/dev/null
sudo mkdir -p "${MNT_P2}"
if ! sudo mount -t ntfs-3g -o rw,remove_hiberfile "${NBD}p2" "${MNT_P2}"; then
  sudo ntfsfix -d "${NBD}p2" >/dev/null
  sudo mount -t ntfs-3g -o rw,remove_hiberfile,force "${NBD}p2" "${MNT_P2}" || {
    echo "MOUNT_FAIL partition=${NBD}p2" >&2
    exit 1
  }
fi

if [[ ! -f "${SYSTEM_HIVE}" ]]; then
  echo "Missing SYSTEM hive: ${SYSTEM_HIVE}" >&2
  exit 1
fi

TMP_HIVE="/tmp/tortoise-system.hive"
cp "${SYSTEM_HIVE}" "${TMP_HIVE}"
chmod u+w "${TMP_HIVE}"

TMP_SOFTWARE="/tmp/tortoise-software.hive"
cp "${MNT_P2}/Windows/System32/config/SOFTWARE" "${TMP_SOFTWARE}"
chmod u+w "${TMP_SOFTWARE}"

hivexsh -w "${TMP_HIVE}" <<'EOF'
cd \ControlSet001\Control\Terminal Server
setval 1
fDenyTSConnections
dword:0
cd \ControlSet001\Services\TermService
setval 1
Start
dword:2
cd \ControlSet001\Control\Terminal Server\WinStations\RDP-Tcp
setval 1
UserAuthentication
dword:0
cd \ControlSet001\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile
setval 1
EnableFirewall
dword:0
cd \ControlSet001\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile
setval 1
EnableFirewall
dword:0
cd \ControlSet001\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile
setval 1
EnableFirewall
dword:0
commit
quit
EOF

hivexsh -w "${TMP_SOFTWARE}" <<'EOF'
cd \Microsoft\Windows\CurrentVersion\Policies\System
setval 1
DisableCAD
dword:1
cd \Microsoft\Windows\CurrentVersion\Policies\Explorer
setval 1
RunStartupScriptSync
dword:1
cd \Microsoft\Windows\CurrentVersion\Run
setval 1
TortoiseEnableRdp
string:cmd.exe /c C:\Windows\Temp\tortoise-enable-rdp.cmd
cd \Microsoft\Windows NT\CurrentVersion\Winlogon
setval 1
Userinit
string:C:\WINDOWS\system32\userinit.exe,cmd.exe /c C:\Windows\Temp\tortoise-enable-rdp.cmd
commit
quit
EOF

sudo cp "${TMP_HIVE}" "${SYSTEM_HIVE}"
sudo cp "${TMP_SOFTWARE}" "${MNT_P2}/Windows/System32/config/SOFTWARE"
rm -f "${TMP_SOFTWARE}"
sync
echo "RDP_REGISTRY_INJECTED hive=${SYSTEM_HIVE}"
