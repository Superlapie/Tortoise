#!/usr/bin/env bash
# Boot sealed base with autologon bootstrap and wait for RDP.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${TORTOISE_QEMU_BASE_IMAGE:-${LAB_ROOT}/base/windows-base.qcow2}"
PID_FILE="${LAB_ROOT}/tmp/finalize-rdp.pid"
RDP_PORT="${TORTOISE_QEMU_RDP_PORT:-53389}"
TIMEOUT_SEC="${1:-2400}"
GUEST_RAM_MB="${TORTOISE_QEMU_GUEST_RAM_MB:-4096}"
GUEST_CPUS="${TORTOISE_QEMU_GUEST_CPUS:-4}"
PROBE="${SCRIPT_DIR}/rdp-probe.py"

rdp_ready() {
  python3 "${PROBE}" "127.0.0.1" "${RDP_PORT}" >/dev/null 2>&1
}

send_monitor() {
  "${SCRIPT_DIR}/qemu-monitor-cmd.sh" "$1" >/dev/null 2>&1 || true
}

pkill -f 'qemu-system-x86_64' 2>/dev/null || true
sleep 3
: > "${LAB_ROOT}/logs/finalize-rdp.log"

CMD=(qemu-system-x86_64 -machine pc,accel=kvm -cpu host -smp "${GUEST_CPUS}" -m "${GUEST_RAM_MB}"
  -drive "file=${BASE},if=ide,index=0,format=qcow2"
  -netdev "user,id=n0,hostfwd=tcp:127.0.0.1:${RDP_PORT}-:3389"
  -device e1000e,netdev=n0
  -boot order=c
  -monitor "tcp:127.0.0.1:4444,server,nowait"
  -display none)

nohup sg kvm -c "$(printf '%q ' "${CMD[@]}")" >> "${LAB_ROOT}/logs/finalize-rdp.log" 2>&1 &
echo $! > "${PID_FILE}"

deadline=$((SECONDS + TIMEOUT_SEC))
while (( SECONDS < deadline )); do
  if rdp_ready; then
    echo "GUEST_REACHABLE rdp=127.0.0.1:${RDP_PORT}"
    send_monitor system_powerdown
    sleep 25
    rm -f "${PID_FILE}"
    exit 0
  fi
  if (( SECONDS == 60 || SECONDS == 120 )); then
    send_monitor sendkey ret
  fi
  if (( SECONDS % 120 == 0 )); then
    echo "$(date -u +%H:%M:%S) waiting seconds=${SECONDS} rdp=$({ rdp_ready && echo up; } || echo down)"
  fi
  sleep 20
done

kill "$(cat "${PID_FILE}")" 2>/dev/null || true
rm -f "${PID_FILE}"
echo "FINALIZE_RDP_TIMEOUT" >&2
exit 1
