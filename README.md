# Tortoise

**Transparent Windows driver management.**

Tortoise is a safety-first Windows driver inventory, health, update, recovery, and diagnostics utility. It tells users exactly what drivers their machine is using, what Windows considers applicable updates, why an update is recommended, where it comes from, what risk it carries, and what will happen before anything is changed.

> Know what is installed. Know what Windows recommends. Know what changes before you approve them.

## Status

**Version:** `0.1.0-alpha` (Batch 0 — repository constitution)

Current capabilities:

- Solution and project structure
- Global mutation-disabled safety gate
- Domain models, policies, and transaction state machine
- Windows device inventory via ConfigMgr/SetupAPI (read-only)
- `tortoise scan` / `tortoise devices` CLI on Windows
- Initial documentation and architecture decision records
- CI scaffolding

Current limitations:

- No driver package inventory yet
- No Windows Update scanning yet
- No driver installation (mutation permanently disabled during early development)
- WPF UI is a placeholder shell

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
