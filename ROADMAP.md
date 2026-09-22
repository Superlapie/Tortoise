# Tortoise Roadmap

This roadmap tracks implementation batches for **Tortoise**, a transparent, safety-first Windows driver management utility.

**Product name:** Tortoise  
**Repository:** `Tortoise`  
**Initial version:** `0.1.0-alpha`

## Safety priority

A cosmetic bug is acceptable. A failed scan is acceptable. An unavailable feature is acceptable. Refusing to install an update is acceptable.

**Damaging the host operating system is not acceptable.**

Mutation (install, stage, remove, restart, disable, enable drivers) remains **globally disabled** until progressive capability gates are satisfied.

## Version milestones

| Version | Theme | Mutation |
|---------|-------|----------|
| v0.1-alpha | Read-only inventory + WUA scan + UI | None |
| v0.2 | Recovery prep, broker, simulated workflow | Simulated only |
| v0.3 | Disposable VM WUA install | VM-gated |
| v0.4 | Controlled physical pilot | Explicit low-risk only |
| v0.5+ | Polish, accessibility, signing maturity | Policy-gated |
| v1.0 | Production exit criteria met | Full policy engine |

## Implementation batches

### Batch 0 — Repository constitution ✅

- Solution, projects, build settings
- License and core docs
- Mutation gate disabled globally
- CI workflows and ADRs
- Initial commit

### Batch 1 — Domain foundation ✅

- Domain models (`DeviceIdentity`, `DriverPackage`, `UpdatePlan`, etc.)
- Error model and update classifications
- Risk model and transaction states
- Policy engine interfaces and default policies
- Unit tests

### Batch 2 — Windows device inventory ✅

- ConfigMgr/SetupAPI wrappers
- Device enumeration, hardware IDs, health/problem codes
- Read-only integration tests

### Batch 3 — Driver package inventory ✅

- Package enumeration and device/package association
- PnPUtil structured fallback (read-only)

### Batch 4 — Windows Update scanning ✅

- WUA wrapper, driver-only search
- Recommended vs optional, managed-policy handling

### Batch 5 — Recommendation engine ✅

- Combine installed driver, applicable update, risk class
- Never mislabel "latest"

### Batch 6 — Production-quality UI ✅

- Overview, Devices, Updates, Safety, History, Settings, About
- Accessibility, dark/light themes

### Batch 7 — Persistence and scan history ✅

- SQLite migrations, scan sessions, diagnostics export

### Batch 8 — Update planning ✅

- Frozen plans, staleness, preflight skeleton
- Fake update transactions (no mutation)

### Batch 9 — Recovery preparation ✅

- Before snapshots, export abstraction, recovery manifest

### Batch 10 — Broker ✅ (current)

- One-shot elevated broker, named pipe IPC, replay protection
- No driver install enabled yet

### Batch 11 — Simulated mutation workflow

- End-to-end plan → preflight → simulated install → verification

### Batch 12 — Disposable VM installation

- Smallest real WUA install path, VM-gated, one low-risk update

### Batch 13 — Fault injection

- Crash/reboot/network/candidate disappearance scenarios

### Batch 14 — Physical pilot readiness

- Pilot checklist, final confirmation UI, recovery docs

### Batch 15 — Release engineering

- Versioning, SBOM, GitHub release pipeline, signing hooks

## CLI preview (future)

```text
tortoise scan
tortoise devices
tortoise updates
tortoise status
tortoise export-report report.json
```

## Features we will probably never build

- Random internet driver search
- Forced unsigned installation
- Secure Boot disable helper
- One-click Driver Store purge
- Automatic BIOS flashing
- Fake health scores or scareware scan results

## Agent operating rules

After each batch:

1. Build Release
2. Run tests
3. Update docs
4. Commit with a clear message
5. Report batch status

**Do not install, update, remove, or mutate any real device driver on a physical machine during autonomous development.**

See the full master build specification in project docs for detailed safety, threat model, and UI requirements.
