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

## Current CI (`c3185cd`)

| Field | Value |
|-------|--------|
| Commit SHA | `c3185cd` |
| Full test workflow | **204 passed** on Windows and Ubuntu (includes Integration tests) |
| Build workflow (filtered) | **198 passed** on Windows and Linux; **8** per-project TRX files aggregated |
| Build | 0 warnings, 0 errors |
| CodeQL | Success — 229/229 C# files scanned |
| Verification summaries | Generated from all TRX inputs; artifact upload green |

**204/204 is numerically correct but does not include real Hyper-V checkpoint/restore.** The `Category=HyperVIntegration` test is opt-in: when `TORTOISE_HARNESS_VM` is unset it returns immediately and counts as passed without touching a VM. Hyper-V lifecycle execution is opt-in and is not exercised by hosted CI.

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

The **`build`** workflow excludes `Category=Integration` and `Category=HyperVIntegration` for fast library validation and verification artifact generation.

The dedicated **`test`** workflow runs the full normal suite on both Windows and Ubuntu, including the Windows process integration tests above.

Hyper-V harness integration (`Category=HyperVIntegration`) is opt-in. Hosted CI does **not** configure `TORTOISE_HARNESS_VM`, so the integration test returns without exercising checkpoint/restore and still reports as passed. Run it locally with an exact VM name when you need real Hyper-V proof.

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
- Opt-in Hyper-V integration tests (`Category=HyperVIntegration`; not exercised by hosted CI — see above)

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

After tests emit TRX files (one TRX per test project in CI):

```bash
mkdir -p artifacts/test-results
for proj in tests/*/*.csproj; do
  case "$(basename "$proj")" in *Probe*|*Integration*) continue ;; esac
  name=$(basename "$proj" .csproj)
  dotnet test "$proj" -c Release --no-build --filter "Category!=Integration&Category!=HyperVIntegration" \
    --logger "trx;LogFileName=${name}.trx" --results-directory artifacts/test-results
done
dotnet run --project tools/Tortoise.VerificationSummary -- \
  --trx-root=artifacts/test-results --output-root=artifacts/verification \
  --sha=$(git rev-parse HEAD) --min-trx-files=8
```

Outputs:

- `artifacts/verification/summary.json`
- `artifacts/verification/summary.md`
