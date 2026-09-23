# Tortoise

[![Build](https://github.com/Superlapie/Tortoise/actions/workflows/build.yml/badge.svg)](https://github.com/Superlapie/Tortoise/actions/workflows/build.yml)
[![CodeQL](https://github.com/Superlapie/Tortoise/actions/workflows/codeql.yml/badge.svg)](https://github.com/Superlapie/Tortoise/actions/workflows/codeql.yml)
[![License: PolyForm Noncommercial](https://img.shields.io/badge/License-PolyForm%20Noncommercial-orange.svg)](LICENSE)
[![Commercial license available](https://img.shields.io/badge/commercial%20use-contact%20author-blue.svg)](COMMERCIAL.md)

**Transparent Windows driver management.**

Tortoise is a safety-first Windows driver inventory, health, update, recovery, and diagnostics utility. It tells users exactly what drivers their machine is using, what Windows considers applicable updates, why an update is recommended, where it comes from, what risk it carries, and what will happen before anything is changed.

> Know what is installed. Know what Windows recommends. Know what changes before you approve them.

## Safety & verification

At commit [`9d046ec`](https://github.com/Superlapie/Tortoise/commit/9d046ec4d966539378a158ab2821ec6a2dd484ed), Tortoise passes **182** automated tests on both GitHub-hosted Windows and Ubuntu runners with **0** build warnings and **0** build errors. CodeQL successfully scans the C# codebase. Windows-specific tests include native device enumeration, Windows Update COM/SDK parity, broker PID binding, cross-process mutation locks, transaction/recovery behavior, and fail-closed security policy.

**Passing tests reduce risk but do not make driver mutation risk-free.**

See [docs/TESTING.md](docs/TESTING.md) for the full test taxonomy, CI source of truth, and candid limits of what automation proves. Batch 16 adds a developer-only disposable-VM harness ([docs/VM_HARNESS.md](docs/VM_HARNESS.md)) for gathering repeatable real-mutation evidence in checkpointed Hyper-V VMs — not on physical machines.

## Status

**Version:** `0.1.0-alpha` (Stabilization + Batch 16 VM harness)

Current capabilities:

- Solution and project structure
- Global mutation-disabled safety gate in public builds
- Domain models, policies, and transaction state machine
- Windows device inventory via ConfigMgr/SetupAPI (read-only)
- Driver store inventory via PnPUtil with device/package association
- Windows Update driver scan via WUA (recommended/optional, managed-policy aware)
- Recommendation engine combining devices, updates, and risk policy
- Frozen update plans with staleness detection, preflight checks, and simulated transactions (no mutation)
- End-to-end simulated mutation workflow: preflight → broker validation → simulated install → package and post-install verification
- VM-gated real Windows Update driver install for one low-risk plan (`tortoise-lab` in disposable VM + explicit env markers only)
- Fault injection scenarios for crash, reboot, network failure, and candidate disappearance with reconciliation guidance
- Physical pilot readiness checklist, confirmation recording, and recovery documentation (no automatic install)
- Stabilization pass: rebuilt Windows interop/WUA, broker v2 plan authority, `Tortoise.Lab` / `tortoise-lab-broker` mutation isolation
- Recovery preparation with before snapshots, export abstraction, and recovery manifest export
- One-shot elevated broker process with PID-bound named pipe IPC, nonce replay protection, and install-only boundary
- Pending reboot fail-closed gate at broker boundary
- WPF app with Overview, Devices, Updates, Safety, Pilot, History, Settings, and About pages
- Light/dark themes, scan coordination, and persistent SQLite scan history
- Redacted JSON diagnostics and recovery manifest export via CLI
- `tortoise broker`, `tortoise recover`, `tortoise plan`, `tortoise workflow`, `tortoise fault`, `tortoise pilot`, and full scan/planning CLI on Windows
- Developer-only Hyper-V VM harness for checkpointed disposable-VM mutation evidence (not in public releases)
- Initial documentation and architecture decision records
- CI scaffolding with machine-readable verification summaries from TRX output

Current limitations:

- No driver installation in public `tortoise` builds — use `tortoise-lab` in an isolated VM only
- Real WUA install requires `tortoise-lab`, `TORTOISE_MUTATION_TESTS=1`, `TORTOISE_ALLOW_VM_INSTALL=1`, and detected guest VM
- Compile-time mutation isolation is incomplete — public code still has broader mutation dependencies than the target architecture
- Plan store uses ACL hardening; cryptographic authorization/sealing remains future work
- Driver package export remains disabled during read-only development
- Physical-machine mutation is not validated; disposable-VM harness evidence is required before expanding the envelope

## Supported platform

- **Windows 11 x64** (target)
- Development builds may be compiled on Linux for libraries; the WPF app builds on Windows runners in CI

## Safety philosophy

Tortoise always prefers:

`refuse > warn > defer > hand off to Windows > make a risky assumption`

See [SAFETY.md](SAFETY.md) for the full safety constitution.

## Build

Requirements:

- .NET 10 SDK
- Windows 11 x64 for full WPF build and Windows integration tests

```bash
dotnet restore Tortoise.slnx
dotnet build Tortoise.slnx -c Release
dotnet test Tortoise.slnx -c Release --filter "Category!=Integration"
```

## Repository layout

```text
src/
  Tortoise.App/            WPF shell
  Tortoise.Core/           Domain logic
  Tortoise.Contracts/      Shared contracts and mutation gate
  Tortoise.Windows/        Windows device/driver interop
  Tortoise.WindowsUpdate/  Windows Update provider
  Tortoise.Security/       Security helpers
  Tortoise.Persistence/    SQLite persistence
  Tortoise.Broker/         Shared broker library
  Tortoise.Lab/            Lab mutation CLI (not in public releases)
  Tortoise.LabBroker/      Elevated Lab broker executable (not in public releases)
  Tortoise.Cli/            Read-only public CLI
tools/
  Tortoise.VmHarness/      Developer-only disposable VM harness
  Tortoise.VerificationSummary/  TRX → verification summary generator
tests/
docs/
```

## Privacy

Tortoise is **local only** by default. See [PRIVACY.md](PRIVACY.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security-sensitive changes require extra review.

- [Commercial licensing](COMMERCIAL.md)

## License

Tortoise uses **dual licensing**:

- **Noncommercial use** — free under the [PolyForm Noncommercial License 1.0.0](LICENSE). You can read, fork, contribute, learn from, and use the project for personal, hobby, educational, and other noncommercial purposes.
- **Commercial use** — requires a separate paid license. See [COMMERCIAL.md](COMMERCIAL.md) for what counts as commercial use and how to contact me.

If you want to ship a commercial product, service, MSP workflow, or client deliverable with Tortoise in the pipeline, get a commercial license first.
