# ADR 0010: Progressive mutation gates

## Status

Accepted

## Context

Real-machine driver mutation during development risks the developer's laptop.

## Decision

Implement `IMutationCapability` with progressive gates: read-only → simulated → VM → physical pilot.

## Consequences

- Default `IsEnabled = false`
- Every mutation path must verify the gate independently
- CI never runs VM mutation tests on hosted runners by default
