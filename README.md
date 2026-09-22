# Tortoise

**Transparent Windows driver management.**

Tortoise is a safety-first Windows driver inventory, health, update, recovery, and diagnostics utility. It tells users exactly what drivers their machine is using, what Windows considers applicable updates, why an update is recommended, where it comes from, what risk it carries, and what will happen before anything is changed.

> Know what is installed. Know what Windows recommends. Know what changes before you approve them.

## Status

**Version:** `0.1.0-alpha` (Batch 13 — fault injection)

Current capabilities:

- Solution and project structure
- Global mutation-disabled safety gate
- Domain models, policies, and transaction state machine
- Windows device inventory via ConfigMgr/SetupAPI (read-only)
- Driver store inventory via PnPUtil with device/package association
- Windows Update driver scan via WUA (recommended/optional, managed-policy aware)
- Recommendation engine combining devices, updates, and risk policy
- Frozen update plans with staleness detection, preflight checks, and simulated transactions (no mutation)
- End-to-end simulated mutation workflow: preflight → broker validation → simulated install → package and post-install verification
- VM-gated real Windows Update driver install for one low-risk plan (disposable VM + explicit env markers only)
- Fault injection scenarios for crash, reboot, network failure, and candidate disappearance with reconciliation guidance
- Recovery preparation with before snapshots, export abstraction, and recovery manifest export
- One-shot elevated broker process with named pipe IPC, nonce replay protection, and allowlisted operations (InstallDriver only in VM-gated mode)
- WPF app with Overview, Devices, Updates, Safety, History, Settings, and About pages
- Light/dark themes, scan coordination, and persistent SQLite scan history
- Redacted JSON diagnostics and recovery manifest export via CLI
- `tortoise broker`, `tortoise recover`, `tortoise plan`, `tortoise workflow`, `tortoise vm`, `tortoise fault`, and full scan/planning CLI on Windows
- Initial documentation and architecture decision records
- CI scaffolding

Current limitations:

- No driver installation on physical machines (mutation disabled unless disposable VM markers are set)
- Real WUA install requires `TORTOISE_MUTATION_TESTS=1`, `TORTOISE_ALLOW_VM_INSTALL=1`, and a detected disposable VM
- UAC elevation launcher not wired yet (broker serve/ping available for development)
- Driver package export remains disabled during read-only development

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
dotnet test Tortoise.slnx -c Release
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
  Tortoise.Broker/         Elevated broker (future)
  Tortoise.Cli/            Read-only CLI (future)
tests/
docs/
```

## Privacy

Tortoise is **local only** by default. See [PRIVACY.md](PRIVACY.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security-sensitive changes require extra review.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
