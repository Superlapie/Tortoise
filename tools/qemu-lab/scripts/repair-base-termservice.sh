#!/usr/bin/env bash
# Restore TermService registry values on ControlSet001 from ControlSet002 backup.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"
SYSTEM_HIVE="${MNT_P2}/Windows/System32/config/SYSTEM"

cleanup() {
  rm -f /tmp/tortoise-system-repair.hive
  sudo umount "${MNT_P2}" 2>/dev/null || true
  sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
}
trap cleanup EXIT

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }
pgrep -f 'qemu-system-x86_64' >/dev/null && {
  echo "Refusing repair while QEMU is running" >&2
  exit 1
}

sudo modprobe nbd max_part=8
sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
sudo qemu-nbd --connect="${NBD}" "${BASE}"
sleep 2
sudo ntfsfix -d "${NBD}p2" >/dev/null
sudo mkdir -p "${MNT_P2}"
sudo mount -t ntfs-3g -o rw,remove_hiberfile,force "${NBD}p2" "${MNT_P2}"

TMP_HIVE="/tmp/tortoise-system-repair.hive"
cp "${SYSTEM_HIVE}" "${TMP_HIVE}"
chmod u+w "${TMP_HIVE}"

hivexsh -w "${TMP_HIVE}" <<'EOF'
cd \ControlSet001\Services\TermService
setval 13
InstanceID
string:b4cf281a-5e5e-4390-830f-5aef3c9
GlassSessionId
dword:1
DependOnService
hex:7:52,00,50,00,43,00,53,00,53,00,00,00,00,00
Description
string:@%SystemRoot%\System32\termsrv.dll,-267
DisplayName
string:@%SystemRoot%\System32\termsrv.dll,-268
ErrorControl
dword:1
FailureActions
hex:3:80,51,01,00,00,00,00,00,00,00,00,00,03,00,00,00,14,00,00,00,01,00,00,00,60,ea,00,00,01,00,00,00,60,ea,00,00,00,00,00,00,60,ea,00,00
ImagePath
string:%SystemRoot%\System32\svchost.exe -k termsvcs
ObjectName
string:NT Authority\NetworkService
RequiredPrivileges
hex:7:53,00,65,00,41,00,73,00,73,00,69,00,67,00,6e,00,50,00,72,00,69,00,6d,00,61,00,72,00,79,00,54,00,6f,00,6b,00,65,00,6e,00,50,00,72,00,69,00,76,00,69,00,6c,00,65,00,67,00,65,00,00,00,53,00,65,00,41,00,75,00,64,00,69,00,74,00,50,00,72,00,69,00,76,00,69,00,6c,00,65,00,67,00,65,00,00,00,53,00,65,00,43,00,68,00,61,00,6e,00,67,00,65,00,4e,00,6f,00,74,00,69,00,66,00,79,00,50,00,72,00,69,00,76,00,69,00,6c,00,65,00,67,00,65,00,00,00,53,00,65,00,43,00,72,00,65,00,61,00,74,00,65,00,47,00,6c,00,6f,00,62,00,61,00,6c,00,50,00,72,00,69,00,76,00,69,00,6c,00,65,00,67,00,65,00,00,00,53,00,65,00,49,00,6d,00,70,00,65,00,72,00,73,00,6f,00,6e,00,61,00,74,00,65,00,50,00,72,00,69,00,76,00,69,00,6c,00,65,00,67,00,65,00,00,00,53,00,65,00,49,00,6e,00,63,00,72,00,65,00,61,00,73,00,65,00,51,00,75,00,6f,00,74,00,61,00,50,00,72,00,69,00,76,00,69,00,6c,00,65,00,67,00,65,00,00,00,00,00
ServiceSidType
dword:1
Start
dword:2
Type
dword:32
commit
quit
EOF

sudo cp "${TMP_HIVE}" "${SYSTEM_HIVE}"
sudo rm -f "${MNT_P2}/Windows/System32/config/SYSTEM.LOG1" "${MNT_P2}/Windows/System32/config/SYSTEM.LOG2"
echo "TERMSERVICE_REPAIRED hive=${SYSTEM_HIVE}"
