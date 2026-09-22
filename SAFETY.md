# Safety Policy

Tortoise operates close to the Windows kernel through device drivers. **Safety is the highest priority of the entire project.**

## Priority order

Tortoise always prefers:

`refuse > warn > defer > hand off to Windows > make a risky assumption`

Never invert this priority.

## Core rules

1. **Never guess driver compatibility.** Matching must come from supported Windows mechanisms or rigorously verified official providers.
2. **No arbitrary driver downloads.** V1 installation source is Windows Update only.
3. **Do not bypass Windows security.** Never disable signature enforcement, Secure Boot, HVCI, Defender, UAC, BitLocker, or update policy.
4. **No unsigned or test-signed production drivers.**
5. **Never manually modify protected driver files or registry state.**
6. **Never force-install a worse-ranked driver** based on version number alone.
7. **No Driver Store cleanup** in initial releases.
8. **Firmware is restricted.** Detect and explain; do not programmatically apply firmware in V1.
9. **Fail closed.** Unknown safety-critical state blocks installation.

## Mutation capability gates

| Level | Environment | Allowed |
|-------|-------------|---------|
| 0 | Read-only (default) | Scan, inventory, plan — no OS mutation |
| 1 | Simulated | Fake backends only |
| 2 | Disposable VM | Checkpointed VM tests only |
| 3 | Physical pilot | Explicit user-selected low-risk device |

During early development, `IMutationCapability.IsEnabled` is **false**. Every mutation path must verify this gate independently.

## V1 restricted install categories

Do not programmatically install:

- Firmware / BIOS / UEFI / TPM / storage firmware
- Storage controllers, RAID, filesystem, encryption drivers
- Boot-critical or unclear rollback chipset drivers
- Hypervisor/security kernel components
- Anything Tortoise cannot confidently categorize

## Recovery

Tortoise prepares recovery information but does not claim guaranteed rollback. Layered recovery includes driver metadata capture, optional package export, and System Restore awareness.

## What Tortoise intentionally refuses

- Third-party driver mirrors and scrapers
- Signature bypass settings in the UI
- Mass "update everything" automation
- Scareware-style urgency language

## Residual risk

Even with conservative policy, driver changes can destabilize a system. Tortoise exists to make those changes **visible, justified, and reversible where possible** — not to eliminate all risk.
