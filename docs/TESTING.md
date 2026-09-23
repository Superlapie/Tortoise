# Testing and verification

This document describes how Tortoise is tested, what automated evidence exists, and what it does **not** prove.

CI on GitHub Actions is the live source of truth for current build/test results. Machine-readable summaries are generated from TRX files into `artifacts/verification/` when CI runs the verification summary tool.

## Last manually sealed baseline

| Field | Value |
|-------|--------|
| Commit SHA | `9d046ec4d966539378a158ab2821ec6a2dd484ed` |
| Test count | 182 passed (Windows and Ubuntu CI) |
| Build | 0 warnings, 0 errors (Windows and Linux library builds) |
| CodeQL | Success — 201/201 C# files scanned |
| Verified on | GitHub-hosted `windows-latest` and `ubuntu-latest` runners |

Batch 16 adds developer-only VM harness tooling and additional harness unit tests on top of this baseline. Passing tests reduce risk but do **not** make driver mutation risk-free.

## Test taxonomy

### Core / domain

- Recommendation logic and risk classification
- Plan freezing, staleness detection, and overwrite protection
- Transaction state transitions and journal validation
- Preflight checks and verification semantics
- Recovery decisions (`AwaitingReboot`, ambiguous `Installing`, stale recovery preparation)

### Broker / security

- Capability token and session validation
- Replay rejection and install-only operation restrictions
- Authoritative persisted-plan reload and canonical hash validation
- Live TOCTOU revalidation at broker boundary
- Missing verifier fail-closed behavior
- PID-bound named pipe behavior (wrong PID disconnect-only; legitimate client can connect afterward)
- Broker policy validation (risk, classification, BrowseOnly, pending reboot)
- Mutation process-role isolation (`tortoise-lab` vs public `tortoise`)

### Windows

- ConfigMgr / SetupAPI inventory sizing and signatures
- Driver store parsing and device/package association
- Device property handling (`DEVPKEY`, `BusTypeGuid`)
- Device disappearance and error-state handling

### Windows Update

- Real WUA scan on Windows CI where applicable
- COM metadata expectations and Windows SDK `wuapi.idl` parity tests
- Mutation-path method signature / dispid verification
- Hardware-ID matching policy
- `AutoSelection` / Windows Recommended mapping
- BrowseOnly fail-closed policy
- Download/install result interpretation and `IsDownloaded` preparation checks

### Persistence

- SQLite migrations
- Frozen plan persistence behavior
- Transaction journals and durable state retrieval

### Integration (opt-in / Windows-only)

- Workflow lock vs servicing lock across real Windows processes
- PID binding across real Windows processes (`Category=Integration`)
- Subprocess probe behavior and timeouts

These integration tests are excluded from default CI via `--filter "Category!=Integration"`.

### Architecture

- Dependency boundary checks where covered by architecture tests
- Mutation gating assumptions in core services
- Public release artifact safety (public packages publish `Tortoise.App` + `Tortoise.Cli` only)

### Static analysis

- CodeQL for C#
- Release dependency inventory (`dependencies.json`) on tagged releases

### Developer VM harness (Batch 16)

- Host-side Hyper-V VM resolution, checkpoint naming, and restore semantics (unit tests)
- Safety gate evaluation, host/guest double-proof blocking, evidence redaction
- Scenario registry and dry-run default behavior
- Opt-in Hyper-V integration tests (`Category=HyperVIntegration`, skipped in ordinary CI)

The VM harness (`tools/Tortoise.VmHarness/`) is **not** part of public release artifacts. It exists to gather repeatable evidence from disposable, checkpointed Hyper-V VMs. See [VM_HARNESS.md](VM_HARNESS.md).

## What automated tests do NOT prove

Be candid about limits:

- **No repeated real driver installations** have yet been proven at scale by the new VM harness unless/until documented harness runs exist in your environment.
- **No physical-machine mutation safety** — physical pilot remains non-mutating readiness recording only.
- **Not every OEM/device topology** is covered by CI scans.
- **Successful rollback cannot be guaranteed** for all drivers or failure modes.
- **Firmware/BIOS/TPM** remain outside programmatic mutation scope.
- **Automated tests are not a security certification** or a guarantee of production readiness.
- **Guest Hyper-V PowerShell Direct** harness execution depends on your VM configuration; CI does not run real disposable-VM mutations by default.

As the VM harness produces evidence, update this section with run IDs, scenarios, and outcomes — not marketing claims.

## Running tests locally

```bash
dotnet restore Tortoise.slnx
dotnet build Tortoise.slnx -c Release
dotnet test Tortoise.slnx -c Release --filter "Category!=Integration"
```

Windows-only integration tests:

```bash
dotnet test Tortoise.slnx -c Release --filter "Category=Integration"
```

Harness unit tests:

```bash
dotnet test tests/Tortoise.VmHarness.Tests/Tortoise.VmHarness.Tests.csproj -c Release
```

## Verification summary tool

After tests emit TRX files:

```bash
dotnet test Tortoise.slnx -c Release --logger "trx;LogFileName=results.trx" --results-directory artifacts/test-results
dotnet run --project tools/Tortoise.VerificationSummary -- --trx-root=artifacts/test-results --output-root=artifacts/verification --sha=$(git rev-parse HEAD)
```

Outputs:

- `artifacts/verification/summary.json`
- `artifacts/verification/summary.md`
