# Threat Model

This document summarizes primary threats and mitigations for Tortoise. It will expand as broker and installation paths are implemented.

## Assets

- Host operating system stability
- Driver integrity and signature enforcement
- User consent and update plans
- Transaction journal and recovery metadata
- Elevated broker authorization tokens

## Threats and mitigations

| Threat | Description | Mitigation |
|--------|-------------|------------|
| Local privilege escalation | Malicious unprivileged process controls broker | Authenticated IPC, nonce, ACL, independent plan reload |
| IPC spoofing | Attacker connects first | User-scoped pipe ACL, session binding |
| Replay | Old privileged request resubmitted | One-time nonce consumption |
| Plan tampering | Target changed after approval | Plan hash, TOCTOU revalidation |
| Command injection | Hostile metadata in device names | Typed DTOs, no shell execution |
| Path traversal | Malicious INF/archive paths | Path validation, reject traversal |
| Reparse points | Symlink/junction attacks | Validate final path, reject unsafe reparse |
| DLL hijacking | Broker loads attacker DLL | Secure DLL search, absolute paths |
| Stale update | Candidate changed after scan | Plan invalidation, rescan |
| Power loss | Laptop dies during install | AC/battery preflight, journaling |
| Process crash | UI/broker crash mid-transaction | Durable journal, reconciliation on restart |
| Concurrent servicing | WU modifies driver simultaneously | Preflight revalidation, single-transaction lock |
| Database tampering | User edits transaction metadata | ACL on privileged records, broker reload |
| Denial of service | Malformed metadata crashes app | Bounded buffers, fuzz testing |

## Residual risk

- A signed driver may still be inappropriate or vulnerable
- Windows Update metadata can be ambiguous
- Recovery paths are not guaranteed

Tortoise documents residual risk rather than hiding it.
