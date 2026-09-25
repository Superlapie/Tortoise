#!/usr/bin/env bash
set -euo pipefail

LAB_ROOT="${TORTOISE_QEMU_LAB_ROOT:-/mnt/tortoise-qemu-lab}"
TEMPLATE="${LAB_ROOT}/config/autounattend.template.xml"
OUT_DIR="${1:-${LAB_ROOT}/tmp/autounattend}"
PASSWORD="${TORTOISE_QEMU_LAB_ADMIN_PASSWORD:-}"

if [[ ! -f "${TEMPLATE}" ]]; then
  REPO_ROOT="${TORTOISE_REPO_ROOT:-/home/lapie/Tortoise}"
  TEMPLATE="${REPO_ROOT}/tools/qemu-lab/windows/autounattend.template.xml"
fi

[[ -f "${TEMPLATE}" ]] || { echo "Missing autounattend template" >&2; exit 1; }

if [[ -z "${PASSWORD}" ]]; then
  PASSWORD="$(openssl rand -base64 18 | tr -d '/+=' | head -c 20)"
fi

mkdir -p "${OUT_DIR}"
TEMPLATE="${TEMPLATE}" PASSWORD="${PASSWORD}" python3 - <<'PY' > "${OUT_DIR}/autounattend.xml"
import os, pathlib, xml.sax.saxutils as x
template = pathlib.Path(os.environ["TEMPLATE"]).read_text()
password = x.escape(os.environ["PASSWORD"])
print(template.replace("*SENSITIVE*", password))
PY

AUTO_ISO="${LAB_ROOT}/tmp/autounattend.iso"
AUTO_FLOP="${LAB_ROOT}/tmp/autounattend.img"
genisoimage -quiet -o "${AUTO_ISO}" -J -r -l -V TORTOISE_UNATTEND "${OUT_DIR}/autounattend.xml"

rm -f "${AUTO_FLOP}"
dd if=/dev/zero of="${AUTO_FLOP}" bs=512 count=2880 status=none
mformat -f 1440 -i "${AUTO_FLOP}" ::
mcopy -i "${AUTO_FLOP}" "${OUT_DIR}/autounattend.xml" ::autounattend.xml

echo "AUTOATTEND_DIR=${OUT_DIR}"
echo "AUTOATTEND_ISO=${AUTO_ISO}"
echo "AUTOATTEND_FLOP=${AUTO_FLOP}"
echo "ADMIN_PASSWORD=${PASSWORD}"
