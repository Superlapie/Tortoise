#!/usr/bin/env bash
# Inject offline RDP bootstrap script, autologon, and delayed logon task.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${1:-${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}}"
CREDS="${LAB_ROOT}/config/lab-credentials.json"
NBD="/dev/nbd0"
MNT_P2="/tmp/tortoise-qemu-p2"
SYSTEM_HIVE="${MNT_P2}/Windows/System32/config/SYSTEM"
PS1="${MNT_P2}/Windows/Temp/tortoise-enable-rdp.ps1"
CMD="${MNT_P2}/Windows/Temp/tortoise-enable-rdp.cmd"

cleanup() {
  rm -f /tmp/tortoise-system.hive /tmp/tortoise-software.hive
  sudo umount "${MNT_P2}" 2>/dev/null || true
  sudo qemu-nbd --disconnect "${NBD}" 2>/dev/null || true
}
trap cleanup EXIT

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }
[[ -f "${CREDS}" ]] || { echo "Missing credentials: ${CREDS}" >&2; exit 1; }
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
if [[ ! -d "${MNT_P2}/Windows" ]]; then
  echo "MOUNT_SANITY_FAIL missing ${MNT_P2}/Windows" >&2
  exit 1
fi

sudo mkdir -p "${MNT_P2}/Windows/Temp"
sudo tee "${PS1}" >/dev/null <<'EOF'
$log = 'C:\Windows\Temp\tortoise-rdp.log'
"=== bootstrap $(Get-Date -Format o) ===" | Out-File $log
Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0
Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp' -Name UserAuthentication -Value 0
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name DisableCAD -Value 1 -Type DWord
Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False -ErrorAction SilentlyContinue
try {
  $tss = Get-CimInstance -Namespace root\cimv2\terminalservices -ClassName Win32_TerminalServiceSetting -ErrorAction Stop
  $result = Invoke-CimMethod -InputObject $tss -MethodName SetAllowTsConnections -Arguments @{ AllowTsConnections = 1; ModifyFirewallException = 1 }
  "SetAllowTsConnections ReturnValue=$($result.ReturnValue)" | Out-File $log -Append
} catch {
  "SetAllowTsConnections error: $_" | Out-File $log -Append
  try {
    $wmi = Get-WmiObject -Class Win32_TerminalServiceSetting -Namespace root\cimv2\terminalservices -ErrorAction Stop
    $rv = $wmi.SetAllowTsConnections(1, 1).ReturnValue
    "SetAllowTsConnections WMI ReturnValue=$rv" | Out-File $log -Append
  } catch {
    "SetAllowTsConnections WMI error: $_" | Out-File $log -Append
  }
}
sc.exe query TermService | Out-File $log -Append
sc.exe start TermService 2>&1 | Out-File $log -Append
for ($i = 0; $i -lt 6; $i++) {
  Start-Sleep -Seconds 10
  if (sc.exe query TermService | Select-String 'RUNNING') { break }
  sc.exe start TermService 2>&1 | Out-File $log -Append
}
sc.exe query TermService | Out-File $log -Append
netstat -an | Select-String 3389 | Out-File $log -Append
New-Item -Path 'HKLM:\SYSTEM\Setup\LabStatus' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SYSTEM\Setup\LabStatus' -Name TortoiseQemuLabReady -Value 1 -PropertyType DWord -Force | Out-Null
if (sc.exe query TermService | Select-String 'RUNNING') {
  New-Item -Path 'C:\Windows\TortoiseLabRdpReady.tag' -ItemType File -Force | Out-Null
}
EOF

sudo tee "${CMD}" >/dev/null <<'EOF'
@echo off
echo === cmd bootstrap %DATE% %TIME% ===>> C:\Windows\Temp\tortoise-rdp.log
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Windows\Temp\tortoise-enable-rdp.ps1 >> C:\Windows\Temp\tortoise-rdp.log 2>&1
EOF

TMP_SOFTWARE="/tmp/tortoise-software.hive"
cp "${MNT_P2}/Windows/System32/config/SOFTWARE" "${TMP_SOFTWARE}"
chmod u+w "${TMP_SOFTWARE}"
hivexsh -w "${TMP_SOFTWARE}" <<'EOF'
cd \Microsoft\Windows\CurrentVersion\Policies\System
setval 1
DisableCAD
dword:1
cd \Microsoft\Windows\CurrentVersion\Policies\Explorer
setval 1
RunStartupScriptSync
dword:1
cd \Microsoft\Windows\CurrentVersion\Reliability
setval 2
ShutdownReasonOn
dword:0
ShutdownReasonUI
dword:0
cd \Microsoft\Windows\CurrentVersion\RunOnce
setval 1
TortoiseEnableRdp
string:cmd.exe /c C:\Windows\Temp\tortoise-enable-rdp.cmd
commit
quit
EOF

python3 "${SCRIPT_DIR}/hive-patch-key.py" "${TMP_SOFTWARE}" \
  '\Microsoft\Windows\CurrentVersion\Run' \
  'TortoiseEnableRdp="cmd.exe /c C:\Windows\Temp\tortoise-enable-rdp.cmd"'

sudo cp "${TMP_SOFTWARE}" "${MNT_P2}/Windows/System32/config/SOFTWARE"
rm -f "${TMP_SOFTWARE}"

TASK_FILE="${MNT_P2}/Windows/System32/Tasks/TortoiseEnableRdp"
sudo python3 - <<'PY' "${TASK_FILE}"
import pathlib, sys
xml = """<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><URI>\\TortoiseEnableRdp</URI></RegistrationInfo>
  <Triggers>
    <BootTrigger><Enabled>true</Enabled><Delay>PT1M</Delay></BootTrigger>
  </Triggers>
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
      <Command>powershell.exe</Command>
      <Arguments>-NoProfile -ExecutionPolicy Bypass -File C:\\Windows\\Temp\\tortoise-enable-rdp.ps1</Arguments>
    </Exec>
  </Actions>
</Task>
"""
pathlib.Path(sys.argv[1]).write_text(xml, encoding="utf-16")
PY

sync
echo "RDP_BOOTSTRAP_INJECTED ps1=${PS1} boot-task"
