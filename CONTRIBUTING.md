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

Never run mutation/install tests on a production workstation. VM-gated mutation tests require explicit environment markers:

```bash
TORTOISE_MUTATION_TESTS=1
TORTOISE_ALLOW_VM_INSTALL=1
# optional override for controlled test harnesses:
TORTOISE_VM_MARKER=1
```

All three conditions (disposable VM detection or marker, mutation tests, VM install opt-in) must be satisfied before real driver installation is enabled.

Fault injection scenarios require `TORTOISE_FAULT_INJECTION=1` and never mutate real drivers — they simulate interrupted workflows for resilience testing.

## Commits

Use clear, focused commits. Do not bundle unrelated changes.
