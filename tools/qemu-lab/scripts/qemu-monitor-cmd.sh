#!/usr/bin/env bash
# Send one QEMU HMP command over the lab monitor TCP socket.
set -euo pipefail

MONITOR_PORT="${TORTOISE_QEMU_MONITOR_PORT:-4444}"
CMD="$*"
if [[ -z "${CMD}" ]]; then
  CMD="info status"
fi

python3 - "${MONITOR_PORT}" "${CMD}" <<'PY'
import socket, sys, time
port, cmd = sys.argv[1], sys.argv[2]
s = socket.create_connection(("127.0.0.1", int(port)), timeout=5)
s.settimeout(1)
buf = b""
for _ in range(20):
    try:
        chunk = s.recv(4096)
        if not chunk:
            break
        buf += chunk
        if b"(qemu)" in buf:
            break
    except socket.timeout:
        if b"(qemu)" in buf:
            break
s.sendall((cmd + "\n").encode("ascii"))
out = b""
deadline = time.time() + 3
while time.time() < deadline:
    try:
        chunk = s.recv(8192)
        if not chunk:
            break
        out += chunk
        if b"(qemu)" in out:
            break
    except socket.timeout:
        if out:
            break
if out:
    text = out.decode("utf-8", errors="replace")
    for line in text.splitlines():
        if line.startswith("(qemu)") or "\x1b[K" in line or "\x1b[D" in line:
            continue
        if line.strip():
            print(line)
s.close()
PY
