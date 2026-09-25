#!/usr/bin/env bash
# Enable autologon and disable shutdown tracker on a profile-ready base image.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
CREDS="${LAB_ROOT}/config/lab-credentials.json"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"

cleanup() {
  rm -f /tmp/tortoise-software-autologon.hive
  sudo umount "${MNT_P2}" 2>/dev/null || true
  sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
}
trap cleanup EXIT

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }
[[ -f "${CREDS}" ]] || { echo "Missing credentials: ${CREDS}" >&2; exit 1; }
pgrep -f 'qemu-system-x86_64' >/dev/null && {
  echo "Refusing inject while QEMU is running" >&2
  exit 1
}

ADMIN_PASSWORD="$(python3 - <<PY
import json
print(json.load(open("${CREDS}"))["adminPassword"])
PY
)"

sudo modprobe nbd max_part=8
sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
sudo qemu-nbd --connect="${NBD}" "${BASE}"
sleep 2
sudo ntfsfix -d "${NBD}p2" >/dev/null
sudo mkdir -p "${MNT_P2}"
if ! timeout 20 sudo mount -t ntfs-3g -o rw,remove_hiberfile,force "${NBD}p2" "${MNT_P2}"; then
  echo "MOUNT_FAIL partition=${NBD}p2" >&2
  exit 1
fi
if [[ ! -d "${MNT_P2}/Windows/System32/config" ]]; then
  echo "MOUNT_SANITY_FAIL missing Windows on ${MNT_P2}" >&2
  exit 1
fi

SHELL_SRC="/home/lapie/Tortoise/tools/qemu-lab/windows/tortoise-shell.c"
[[ -f "${SHELL_SRC}" ]] || SHELL_SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")/../windows" && pwd)/tortoise-shell.c"
x86_64-w64-mingw32-gcc -O2 -s -o /tmp/tortoise-shell.exe "${SHELL_SRC}" -ladvapi32 -lkernel32
sudo cp /tmp/tortoise-shell.exe "${MNT_P2}/Windows/Temp/tortoise-shell.exe"

sudo tee "${MNT_P2}/Windows/Temp/tortoise-shell.cmd" >/dev/null <<'EOF'
@echo off
echo === shell %DATE% %TIME% ===>> C:\Windows\Temp\tortoise-rdp.log
start "" C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Windows\Temp\tortoise-enable-rdp.ps1
start explorer.exe
EOF

TMP_SOFTWARE="/tmp/tortoise-software-autologon.hive"
cp "${MNT_P2}/Windows/System32/config/SOFTWARE" "${TMP_SOFTWARE}"
chmod u+w "${TMP_SOFTWARE}"

hivexsh -w "${TMP_SOFTWARE}" <<EOF
cd \Microsoft\Windows NT\CurrentVersion\Winlogon
setval 26
AutoRestartShell
dword:1
Background
string:0 0 0
CachedLogonsCount
string:10
DebugServerCommand
string:no
DefaultDomainName
string:.
DefaultPassword
string:${ADMIN_PASSWORD}
DefaultUserName
string:Administrator
AutoAdminLogon
string:1
DisableBackButton
dword:1
ForceUnlockLogon
dword:0
LegalNoticeCaption
string:
LegalNoticeText
string:
PasswordExpiryWarning
dword:5
PowerdownAfterShutdown
string:0
ReportBootOk
string:1
Shell
string:explorer.exe
ShellAppRuntime
string:ShellAppRuntime.exe
ShellCritical
dword:0
ShellInfrastructure
string:sihost.exe
Userinit
string:C:\WINDOWS\system32\userinit.exe,C:\Windows\Temp\tortoise-shell.exe,
VMApplet
string:SystemPropertiesPerformance.exe /pagefile
WinStationsDisabled
string:0
EnableSIHostIntegration
dword:1
scremoveoption
string:0
DisableCAD
dword:1
ShutdownFlags
dword:39
commit
quit
EOF

hivexsh -w "${TMP_SOFTWARE}" <<'EOF'
cd \Microsoft\Windows\CurrentVersion\Reliability
setval 2
ShutdownReasonOn
dword:0
ShutdownReasonUI
dword:0
commit
quit
EOF

sudo cp "${TMP_SOFTWARE}" "${MNT_P2}/Windows/System32/config/SOFTWARE"
sudo rm -f "${MNT_P2}/Windows/System32/config/SOFTWARE.LOG1" "${MNT_P2}/Windows/System32/config/SOFTWARE.LOG2"
[[ -f "${MNT_P2}/Windows/Temp/tortoise-shell.exe" ]] || { echo "EXE_MISSING" >&2; exit 1; }
echo "AUTOLOGON_INJECTED user=Administrator shell_exe=${MNT_P2}/Windows/Temp/tortoise-shell.exe"
