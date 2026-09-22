# Contributing to Tortoise

Thank you for contributing. Tortoise is security-sensitive software; conservative changes are preferred.

## Pull request checklist

Security-sensitive areas (broker, installation, IPC, signature verification, risk policy, recovery, firmware policy) require:

- [ ] No new privileged capability without ADR
- [ ] No arbitrary command execution
- [ ] No security feature disabled
- [ ] Tests include refusal/negative cases
- [ ] Source provenance documented
- [ ] Safety documentation updated when behavior changes

## Development setup

```bash
dotnet restore Tortoise.slnx
dotnet build Tortoise.slnx -c Release
dotnet test Tortoise.slnx -c Release
```

## Coding standards

- Nullable reference types enabled
- Async/await in UI-facing work
- No business logic in WPF code-behind
- No `catch { }` empty handlers
- Prefer official Microsoft documentation for Windows API behavior

## Mutation testing

Never run mutation/install tests on a production workstation. VM-gated mutation tests require explicit environment markers and **Tortoise.Lab**:

```bash
TORTOISE_MUTATION_TESTS=1
TORTOISE_ALLOW_VM_INSTALL=1
tortoise-lab vm install <plan-id>
```

`TORTOISE_VM_MARKER` is removed — environment variables must never substitute for detected guest VM evidence.

All three conditions (detected guest VM, mutation tests marker, VM install opt-in) must be satisfied before real driver installation is enabled, and only through `tortoise-lab`.

Fault injection scenarios require `TORTOISE_FAULT_INJECTION=1` and never mutate real drivers — they simulate interrupted workflows for resilience testing.

Physical pilot readiness requires explicit markers on a non-VM host:

```bash
TORTOISE_MUTATION_TESTS=1
TORTOISE_PHYSICAL_PILOT=1
```

Pilot checklist and confirmation record readiness only. They do not install drivers automatically.

See [docs/PHYSICAL_PILOT_RECOVERY.md](docs/PHYSICAL_PILOT_RECOVERY.md) before any physical pilot work.

## Commits

Use clear, focused commits. Do not bundle unrelated changes.
