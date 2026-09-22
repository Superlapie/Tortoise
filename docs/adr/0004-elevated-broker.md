# ADR 0004: Elevated broker

## Status

Accepted

## Context

Driver installation requires elevation; the main UI must not run as Administrator.

## Decision

Use `Tortoise.Broker` as a short-lived elevated process with authenticated, allowlisted IPC.

## Consequences

- IPC security becomes critical path
- Threat model and negative tests required before install enablement
