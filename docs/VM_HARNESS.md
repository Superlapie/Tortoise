# Disposable VM mutation harness

> **THIS HARNESS IS FOR DISPOSABLE, CHECKPOINTED VIRTUAL MACHINES ONLY.**
>
> Do **not** run real-mutation harness commands on your normal Windows installation or any machine you cannot afford to restore from snapshot.

## Purpose

`tortoise-vm-harness` is a **developer-only** orchestrator for exercising the real Tortoise.Lab driver mutation lifecycle inside an expendable Hyper-V VM:

1. Resolve and positively identify the target VM on the host
2. Create a dedicated checkpoint before any mutation boundary
3. Collect guest evidence via the same Tortoise.Lab code paths used in production Lab workflows
4. Optionally execute a real low-risk Windows Update driver install when explicitly authorized
5. Restore the pre-run checkpoint and verify the VM becomes reachable again
6. Write sanitized evidence under `artifacts/vm-harness/<run-id>/`

Public `tortoise` builds remain read-only. This harness is **not** included in release ZIP artifacts.

## Threat and safety model

Physical-machine mutation is prohibited.

Real mutation requires **all** of the following to be independently true:

1. Target is a positively identified Hyper-V guest VM
2. Host harness identifies the VM through Hyper-V (`VM ID` recorded)
3. A dedicated checkpoint exists with a recorded identifier for this run
4. Guest `WindowsExecutionEnvironmentDetector` reports `IsDisposableVm = true`
5. Lab mutation gates satisfied (`TORTOISE_MUTATION_TESTS`, `TORTOISE_ALLOW_VM_INSTALL`, capability resolver)
6. Target plan is frozen
7. Update is low risk
8. Update is Windows Recommended
9. Source is Windows Update
10. Authoritative hardware-ID match
11. BrowseOnly known and false
12. Package downloaded and verified before elevation
13. Pending reboot known and `NotPending`
14. Broker final live TOCTOU checks pass
15. Servicing lock acquired
16. No restricted device category involved

Any unknown safety-critical state **blocks** the operation. There is no `--force`, `--ignore-safety`, or similar bypass flag.

`--execute-disposable-vm-mutation` is **consent**, not proof of safety.

## Supported provider

| Provider | Status |
|----------|--------|
| Hyper-V | Implemented (`HyperVVmHarnessProvider`) |
| VMware / VirtualBox / cloud / SSH | Not implemented in Batch 16 |

There is **no default VM name**. You must pass `--vm <exact-name>`.

## Requirements

### Host

- Windows with Hyper-V management tools
- Hyper-V PowerShell module available
- Permission to create/restore VM checkpoints
- Tortoise repo built locally

### Guest

- Hyper-V guest with integration services
- Tortoise.Lab (`tortoise-lab`, `tortoise-lab-broker`) installed
- Guest VM detection via Hyper-V guest registry parameters
- Lab environment markers enabled when running mutation
- PowerShell Direct credentials supplied to the harness host via environment variables (never committed):

  - `TORTOISE_VM_HARNESS_GUEST_USER`
  - `TORTOISE_VM_HARNESS_GUEST_PASSWORD`

Credentials are **never** written to run manifests, logs, or the repository.

## Commands

Inspect host VM resolution (no checkpoint, no mutation):

```powershell
dotnet run --project tools/Tortoise.VmHarness -- inspect --vm MyDisposableLabVm
```

Dry-run a scenario (default — no checkpoint, no mutation):

```powershell
dotnet run --project tools/Tortoise.VmHarness -- run --vm MyDisposableLabVm --scenario baseline-low-risk-install
```

Prove checkpoint create/restore **without** mutation:

```powershell
dotnet run --project tools/Tortoise.VmHarness -- run --vm MyDisposableLabVm --scenario baseline-low-risk-install --prove-checkpoint-restore
```

Real disposable-VM mutation (explicit flag, all gates still mandatory):

```powershell
dotnet run --project tools/Tortoise.VmHarness -- run --vm MyDisposableLabVm --scenario baseline-low-risk-install --execute-disposable-vm-mutation
```

List registered scenarios:

```powershell
dotnet run --project tools/Tortoise.VmHarness -- list-scenarios
```

## VM identity verification

### Host proof

Hyper-V resolves the VM by **exact name** (case-sensitive equality). Ambiguous or missing names are rejected. The harness records:

- VM name
- Hyper-V VM ID (GUID)
- VM power state
- Checkpoint name / ID / creation time

The guest is never trusted to prove a checkpoint exists.

### Guest proof

The harness runs `tortoise-lab status` in the guest (when transport is configured) and records `IsDisposableVm` and capability resolver output.

If host and guest disagree → **BLOCK** (no inference about which side is correct).

## Evidence directory

Each run creates a directory under `artifacts/vm-harness/<run-id>/`.

### Always emitted (when the corresponding phase runs)

| File | When |
|------|------|
| `run.json` | Every run |
| `preflight.json` | Every run (host target + host pre-gate on checkpoint paths) |
| `scenario.json` | Scenario runs |
| `checkpoint.json` | Checkpoint create/restore paths |
| `restore-result.json` | After checkpoint creation (cleanup phase) |
| `guest-environment.json` | When guest transport is configured |
| `verification.json` | Host/guest proof on checkpoint paths |
| `plan.json` | Baseline candidate discovery |
| `preflight.json` | Baseline preflight (guest) |
| `broker-result.json` | Host pre-gate and/or inner Lab gate evidence |
| `REPORT.md` | Every run |
| `verification.json` (manifest) | Final evidence manifest |

### Emitted by baseline mutation path when guest transport is configured

| File | Content |
|------|---------|
| `device-before.json` / `device-after.json` | Sanitized diagnostics export snapshot |
| `wua-before.json` / `wua-after.json` | Same export snapshot (WUA/recommendation sections) |
| `transactions-before.json` / `transactions-after.json` | `tortoise recover list` output for the harness Lab DB |

Dry-run and checkpoint-proof runs do not claim to emit the before/after mutation evidence set.

## Restore behavior

A harness run is not complete when installation finishes. The host **must** restore the pre-run checkpoint afterward.

Report separates:

- **Tortoise mutation outcome** (succeeded / failed / blocked / not attempted)
- **VM restoration outcome** (succeeded / failed / skipped)

Restore failure yields verdict `RESTORE_FAILED` even if mutation appeared to succeed.

Checkpoints use names like:

`TortoiseHarness-<UTC timestamp>-<run-id-prefix>`

Unrelated checkpoints are not reused silently.

## Scenarios

| ID | Purpose |
|----|---------|
| `baseline-low-risk-install` | Discover eligible low-risk WU driver; real install only with execute flag |
| `checkpoint-restore-proof` | Orchestrator checkpoint lifecycle without mutation |
| `abort-before-elevation` | Registered fault scenario (not auto-run) |
| `broker-launch-failure` | Registered fault scenario |
| `stale-plan` | Registered fault scenario |
| `candidate-disappears` | Registered fault scenario |
| `prepared-package-missing` | Registered fault scenario |
| `pending-reboot` | Registered fault scenario |
| `servicing-lock-contention` | Registered fault scenario |
| `wrong-pid-client` | Registered fault scenario |
| `crash-after-installing-checkpoint` | Registered fault scenario |
| `awaiting-reboot-resume` | Registered fault scenario |
| `post-install-inconclusive` | Registered fault scenario |

`NO_ELIGIBLE_CANDIDATE` is a successful harness outcome when no policy-eligible driver exists — the harness must not weaken selection to manufacture a test.

## Verdicts

| Verdict | Meaning |
|---------|---------|
| `PASS` | Mutation succeeded (if attempted) and restore succeeded |
| `FAIL_CLOSED` | Tortoise or harness safety blocked/failed mutation |
| `NO_ELIGIBLE_CANDIDATE` | No eligible driver; no mutation |
| `RECOVERY_REQUIRED` | Inconclusive verification or unfinished reboot reconciliation |
| `RESTORE_FAILED` | Checkpoint restore failed — serious harness failure |
| `HARNESS_ERROR` | Orchestration/checkpoint/transport failure |
| `DRY_RUN_COMPLETE` | Default dry-run finished without mutation |
| `CHECKPOINT_RESTORE_PROVED` | Checkpoint create/restore succeeded without mutation |

Never report plain `PASS` if restore failed, verification is inconclusive, or reboot reconciliation is unfinished.

## First real run policy

Do **not** run `--execute-disposable-vm-mutation` until:

1. Existing sealed baseline tests remain green
2. Harness unit tests pass
3. Windows + Ubuntu CI green
4. CodeQL green
5. Dry-run against your disposable VM succeeds
6. `--prove-checkpoint-restore` succeeds
7. Guest Lab detection succeeds
8. Evidence collection works
9. Host and guest VM proofs agree

If no suitable disposable Hyper-V VM exists, stop at harness-ready state: **AWAITING DISPOSABLE VM EXECUTION**.

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| `Hyper-V PowerShell module is not available` | Hyper-V tools not installed on host |
| `Ambiguous Hyper-V VM name` | Duplicate display names — rename VM |
| Guest transport not configured | Missing `TORTOISE_VM_HARNESS_GUEST_*` env vars |
| Host/guest proof disagree | Guest not running under Hyper-V guest parameters |
| `NO_ELIGIBLE_CANDIDATE` | No low-risk Windows Recommended WU driver applicable — expected |
| Restore failed | Inspect `restore-result.json` and Hyper-V checkpoint permissions |

## Cleanup

- Evidence remains under `artifacts/vm-harness/` for inspection; delete when no longer needed.
- Checkpoints created by the harness remain on the VM until you remove them manually after verifying restore.
- Never store guest passwords in the repo or run manifests.

## What never runs on physical machines

- `--execute-disposable-vm-mutation`
- Checkpoint restore testing against non-VM targets
- Harness-orchestrated `tortoise-lab vm install`

Physical pilot CLI flows remain **non-mutating** readiness recording only.
