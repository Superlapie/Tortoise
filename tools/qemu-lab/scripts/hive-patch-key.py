#!/usr/bin/env python3
"""Patch selected values on a multi-value Windows registry key without wiping siblings."""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

DWORD_RE = re.compile(r'^"([^"]+)"=dword:([0-9a-fA-F]+)$')
STRING_RE = re.compile(r'^"([^"]+)"="(.*)"$')
STRING2_RE = re.compile(r'^"([^"]+)"=str\(\d+\):"(.*)"$')
HEX_RE = re.compile(r'^"([^"]+)"=hex(?::(\d+))?:([0-9a-fA-F,]+)$')


def run_hivexsh(hive: Path, commands: str, write: bool = False) -> str:
    cmd = ["hivexsh"]
    if write:
        cmd.append("-w")
    cmd.append(str(hive))
    proc = subprocess.run(
        cmd,
        input=commands,
        text=True,
        capture_output=True,
        check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr.strip() or proc.stdout.strip() or "hivexsh failed")
    return proc.stdout


def parse_lsval(output: str) -> list[tuple[str, str, str]]:
    values: list[tuple[str, str, str]] = []
    for line in output.splitlines():
        line = line.strip()
        if not line:
            continue
        match = DWORD_RE.match(line)
        if match:
            name, hex_value = match.groups()
            values.append((name, "dword", str(int(hex_value, 16))))
            continue
        match = STRING_RE.match(line)
        if match:
            name, value = match.groups()
            values.append((name, "string", value))
            continue
        match = STRING2_RE.match(line)
        if match:
            name, value = match.groups()
            values.append((name, "string", value.replace("\\\\", "\\")))
            continue
        match = HEX_RE.match(line)
        if match:
            name, _count, payload = match.groups()
            values.append((name, "hex", payload))
            continue
        raise RuntimeError(f"unsupported registry value line: {line}")
    return values


def format_setval(values: list[tuple[str, str, str]]) -> str:
    lines = [f"setval {len(values)}"]
    for name, kind, value in values:
        lines.append(name)
        if kind == "dword":
            lines.append(f"dword:{value}")
        elif kind == "string":
            lines.append(f"string:{value}")
        else:
            lines.append(f"hex:{value}")
    return "\n".join(lines)


def patch_key(hive: Path, key: str, patches: dict[str, int | str]) -> None:
    lsval = run_hivexsh(hive, f"cd {key}\nlsval\nquit\n")
    values = parse_lsval(lsval)
    index = {name: idx for idx, (name, _, _) in enumerate(values)}
    for name, patch in patches.items():
        if isinstance(patch, int):
            if name not in index:
                values.append((name, "dword", str(patch)))
            else:
                _, _, _old = values[index[name]]
                values[index[name]] = (name, "dword", str(patch))
        else:
            if name not in index:
                values.append((name, "string", patch))
            else:
                values[index[name]] = (name, "string", patch)

    body = f"cd {key}\n{format_setval(values)}\ncommit\nquit\n"
    run_hivexsh(hive, body, write=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("hive")
    parser.add_argument("key", help=r"Registry key path, e.g. \ControlSet001\Control\Terminal Server\WinStations\RDP-Tcp")
    parser.add_argument("patch", nargs="+", help="name=value where value is decimal dword or quoted string")
    args = parser.parse_args()

    patches: dict[str, int | str] = {}
    for item in args.patch:
        name, raw = item.split("=", 1)
        if (raw.startswith('"') and raw.endswith('"')) or (raw.startswith("'") and raw.endswith("'")):
            patches[name] = raw[1:-1]
        else:
            patches[name] = int(raw, 0)

    hive = Path(args.hive)
    if not hive.is_file():
        print(f"missing hive: {hive}", file=sys.stderr)
        return 1

    work = hive
    tmp: Path | None = None
    if not os.access(hive, os.W_OK):
        tmp = Path(tempfile.mkstemp(prefix="hive-patch-", suffix=".hive")[1])
        tmp.write_bytes(hive.read_bytes())
        tmp.chmod(0o666)
        work = tmp

    try:
        patch_key(work, args.key, patches)
        if tmp is not None:
            tmp.replace(hive)
    finally:
        if tmp is not None and tmp.exists() and tmp != hive:
            tmp.unlink(missing_ok=True)

    for name, value in patches.items():
        print(f"PATCHED {name}={value}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
