# ADR 0006: Firmware restricted

## Status

Accepted

## Context

Firmware updates can brick hardware and are not ordinary drivers.

## Decision

Detect and classify firmware-related updates as restricted; do not programmatically apply in V1.

## Consequences

- Users may need Windows/OEM experiences for firmware
- Clear messaging required in UI
