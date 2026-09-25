#!/usr/bin/env bash
# Boot installed base/overlay with autounattend floppy to finish OOBE, wait for RDP.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}"
OVERLAY="${TORTOISE_QEMU_OVERLAY_IMAGE:-${LAB_ROOT}/overlays/finish-oobe.qcow2}"
AUTO_FLOP="${LAB_ROOT}/tmp/autounattend.img"
PID_FILE="${LAB_ROOT}/tmp/finish-oobe.pid"
MONITOR_PORT="${TORTOISE_QEMU_MONITOR_PORT:-4444}"
TIMEOUT_SEC="${1:-1800}"
RDP_PORT="${TORTOISE_QEMU_RDP_PORT:-53389}"
GUEST_RAM_MB="${TORTOISE_QEMU_GUEST_RAM_MB:-4096}"
GUEST_CPUS="${TORTOISE_QEMU_GUEST_CPUS:-4}"

export TORTOISE_QEMU_LAB_ROOT="${LAB_ROOT}"

pkill -f 'qemu-system-x86_64' 2>/dev/null || true
sleep 3

[[ -f "${BASE}" ]] || { echo "Missing base: ${BASE}" >&2; exit 1; }
[[ -f "${AUTO_FLOP}" ]] || "${SCRIPT_DIR}/prepare-autounattend.sh" >/dev/null

if [[ ! -f "${OVERLAY}" ]]; then
  qemu-img create -f qcow2 -F qcow2 -b "${BASE}" "${OVERLAY}"
fi

CMD=(qemu-system-x86_64 -machine pc,accel=kvm -cpu host -smp "${GUEST_CPUS}" -m "${GUEST_RAM_MB}"
  -drive "file=${OVERLAY},if=ide,index=0,format=qcow2"
  -drive "file=${AUTO_FLOP},if=floppy,format=raw,readonly=on"
  -netdev "user,id=n0,hostfwd=tcp:127.0.0.1:${RDP_PORT}-:3389"
  -device e1000,netdev=n0
  -boot order=c
  -monitor "tcp:127.0.0.1:${MONITOR_PORT},server,nowait"
  -display none)

nohup sg kvm -c "$(printf '%q ' "${CMD[@]}")" >> "${LAB_ROOT}/logs/finish-oobe.log" 2>&1 &
echo $! > "${PID_FILE}"
sleep 5

send_enter() {
  "${SCRIPT_DIR}/qemu-monitor-cmd.sh" sendkey ret >/dev/null 2>&1 || true
}

deadline=$((SECONDS + TIMEOUT_SEC))
sent=0
while (( SECONDS < deadline )); do
  if python3 - <<PY
import socket
s = socket.socket()
s.settimeout(3)
try:
    s.connect(("127.0.0.1", int("${RDP_PORT}")))
    data = s.recv(4)
    raise SystemExit(0 if data.startswith(b"\x03\x00") else 1)
except Exception:
    raise SystemExit(1)
finally:
    s.close()
PY
  then
    kill "$(cat "${PID_FILE}")" 2>/dev/null || true
    wait "$(cat "${PID_FILE}")" 2>/dev/null || true
    rm -f "${PID_FILE}"
    echo "OOBE_FINISH_OK"
    exit 0
  fi
  if (( sent < 5 && SECONDS > 120 )); then
    send_enter
    sent=$((sent + 1))
    sleep 30
  fi
  sleep 20
done

kill "$(cat "${PID_FILE}")" 2>/dev/null || true
rm -f "${PID_FILE}"
echo "OOBE_FINISH_TIMEOUT" >&2
exit 1
