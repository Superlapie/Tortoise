#!/usr/bin/env bash
# Install a machine startup script via local Group Policy paths on the base image.
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"
GPO_DIR="${MNT_P2}/Windows/System32/GroupPolicy/Machine/Scripts"
GPO_STARTUP="${GPO_DIR}/Startup"
TARGET_CMD="${MNT_P2}/Windows/Temp/tortoise-enable-rdp.cmd"
CMD_NAME="tortoise-enable-rdp.cmd"

cleanup() {
  sudo umount "${MNT_P2}" 2>/dev/null || true
  sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
}
trap cleanup EXIT

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }
pgrep -f 'qemu-system-x86_64' >/dev/null && {
  echo "Refusing inject while QEMU is running" >&2
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
if [[ ! -d "${MNT_P2}/Windows" ]]; then
  echo "MOUNT_SANITY_FAIL missing ${MNT_P2}/Windows" >&2
  exit 1
fi

sudo mkdir -p "${GPO_STARTUP}" "${MNT_P2}/Windows/Temp" "${MNT_P2}/ProgramData/TortoiseLab"
sudo tee "${MNT_P2}/ProgramData/TortoiseLab/tortoise-enable-rdp.cmd" "${TARGET_CMD}" "${GPO_STARTUP}/${CMD_NAME}" >/dev/null <<'EOF'
@echo off
echo === cmd bootstrap %DATE% %TIME% ===>> C:\Windows\Temp\tortoise-rdp.log
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Windows\Temp\tortoise-enable-rdp.ps1 >> C:\Windows\Temp\tortoise-rdp.log 2>&1
EOF

sudo tee "${GPO_DIR}/scripts.ini" >/dev/null <<EOF
[Startup]
0CmdLine=${CMD_NAME}
0Parameters=
EOF

TASK_FILE="${MNT_P2}/Windows/System32/Tasks/TortoiseEnableRdp"
sudo python3 - <<'PY' "${TASK_FILE}"
import pathlib, sys
xml = """<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><URI>\\TortoiseEnableRdp</URI></RegistrationInfo>
  <Triggers><BootTrigger><Enabled>true</Enabled></BootTrigger></Triggers>
  <Principals>
    <Principal id="Author"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>cmd.exe</Command>
      <Arguments>/c C:\\Windows\\Temp\\tortoise-enable-rdp.cmd</Arguments>
    </Exec>
  </Actions>
</Task>
"""
pathlib.Path(sys.argv[1]).write_text(xml, encoding="utf-16")
PY

TMP_SOFTWARE="/tmp/tortoise-gpo-software.hive"
cp "${MNT_P2}/Windows/System32/config/SOFTWARE" "${TMP_SOFTWARE}"
chmod u+w "${TMP_SOFTWARE}"
hivexsh -w "${TMP_SOFTWARE}" <<'EOF' || true
cd \Microsoft\Windows\CurrentVersion\Group Policy
add Scripts
cd \Microsoft\Windows\CurrentVersion\Group Policy\Scripts
add Startup
cd \Microsoft\Windows\CurrentVersion\Group Policy\Scripts\Startup
add 0
cd \Microsoft\Windows\CurrentVersion\Group Policy\State
add Machine
cd \Microsoft\Windows\CurrentVersion\Group Policy\State\Machine
add Scripts
cd \Microsoft\Windows\CurrentVersion\Group Policy\State\Machine\Scripts
add Startup
cd \Microsoft\Windows\CurrentVersion\Group Policy\State\Machine\Scripts\Startup
add 0
commit
quit
EOF
hivexsh -w "${TMP_SOFTWARE}" <<'EOF'
cd \Microsoft\Windows\CurrentVersion\Group Policy\Scripts\Startup\0
setval 3
Script
string:tortoise-enable-rdp.cmd
Parameters
string:
IsPowershell
dword:0
cd \Microsoft\Windows\CurrentVersion\Group Policy\State\Machine\Scripts\Startup\0
setval 4
Script
string:tortoise-enable-rdp.cmd
Parameters
string:
IsPowershell
dword:0
ExecTime
dword:0
commit
quit
EOF
sudo cp "${TMP_SOFTWARE}" "${MNT_P2}/Windows/System32/config/SOFTWARE"
rm -f "${TMP_SOFTWARE}"

sync
echo "GPO_STARTUP_INJECTED script=${TARGET_CMD} task=${TASK_FILE}"
