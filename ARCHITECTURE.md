# Architecture

## Overview

Tortoise is a layered .NET 10 WPF application for Windows driver inventory and update management.

```text
Tortoise.App (WPF, standard user)
        │
        ▼
Tortoise.Core (domain, policy, planning)
        │
   ┌────┴────┬──────────────┬─────────────┐
   ▼         ▼              ▼             ▼
Windows  WindowsUpdate  Persistence   Security
   │         │              │             │
   └────┬────┴──────────────┴─────────────┘
        ▼
Tortoise.Contracts (shared DTOs, mutation gate)

Tortoise.Broker (elevated, short-lived) ── IPC ── App
```

## Dependency rules

- **Tortoise.Contracts** — no references to implementation layers
- **Tortoise.Core** — no references to App, Windows, Broker, or UI
- **Tortoise.App** — composition root; no business logic in code-behind
- **Tortoise.Broker** — no WPF; minimal surface; allowlisted operations only

## Mutation gate

`IMutationCapability` is checked at every mutation boundary. Default: disabled (`MutationCapability.ReadOnly`).

## Elevation model

The GUI runs as a standard user. Privileged operations use a short-lived elevated broker over authenticated IPC with nonce, plan hash validation, and replay protection.

## Persistence

SQLite with explicit migrations for scan sessions, transactions, and recovery snapshots. Stored under `%ProgramData%\Tortoise\` with appropriate ACLs.

## Windows integration

Device enumeration via Configuration Manager and SetupAPI. Driver updates via Windows Update Agent in V1.

See `docs/adr/` for recorded architecture decisions.
