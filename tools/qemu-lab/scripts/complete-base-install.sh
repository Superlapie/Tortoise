#!/usr/bin/env bash
# Wait for install VM RDP while floppy is still attached, inject Panther answer file, seal base.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
BASE="${LAB_ROOT}/base/windows-base.qcow2"
AUTO_XML="${LAB_ROOT}/tmp/autounattend/autounattend.xml"
FINGERPRINT="${LAB_ROOT}/config/base-image.sha256"
FINALIZE_TIMEOUT="${1:-1800}"

export TORTOISE_QEMU_LAB_ROOT="${LAB_ROOT}"
export TORTOISE_QEMU_BASE_IMAGE="${BASE}"

[[ -f "${BASE}" ]] || { echo "Missing base image: ${BASE}" >&2; exit 1; }

"${SCRIPT_DIR}/stop-windows-install.sh" || true
pkill -f 'qemu-system-x86_64' 2>/dev/null || true
sleep 3

"${SCRIPT_DIR}/inject-base-autounattend.sh" "${BASE}" "${AUTO_XML}"
"${SCRIPT_DIR}/inject-base-rdp-bootstrap.sh" "${BASE}"
"${SCRIPT_DIR}/inject-base-rdp-service.sh" "${BASE}"
"${SCRIPT_DIR}/repair-base-termservice.sh" "${BASE}" || true
"${SCRIPT_DIR}/inject-base-rdp-offline-system.sh" "${BASE}"
"${SCRIPT_DIR}/inject-base-gpo-startup.sh" "${BASE}"
"${SCRIPT_DIR}/inject-base-rdp-startup.sh" "${BASE}"
"${SCRIPT_DIR}/inject-base-autologon.sh" "${BASE}"
"${SCRIPT_DIR}/verify-base-bootable.sh" "${BASE}"
"${SCRIPT_DIR}/finalize-base-rdp.sh" "${FINALIZE_TIMEOUT}"

sha256sum "${BASE}" | awk '{print $1}' | tee "${FINGERPRINT}" >/dev/null
cat > "${LAB_ROOT}/artifacts/base-install-complete.json" <<EOF
{
  "completedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "baseImagePath": "${BASE}",
  "baseSha256": "$(cat "${FINGERPRINT}")",
  "baseBytes": $(stat -c%s "${BASE}"),
  "status": "BASE_INSTALL_SEALED"
}
EOF

echo "BASE_INSTALL_SEALED sha256=$(cat "${FINGERPRINT}") bytes=$(stat -c%s "${BASE}")"
