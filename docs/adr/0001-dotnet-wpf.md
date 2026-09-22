# ADR 0001: .NET 10 and WPF

## Status

Accepted

## Context

Tortoise is a native Windows desktop utility requiring rich UI, MVVM, and deep Windows API integration.

## Decision

Use C# on .NET 10 LTS with WPF and MVVM (CommunityToolkit.Mvvm).

## Consequences

- Windows-only UI stack
- CI requires Windows runners for full App build
- Strong typing and mature Windows interop ecosystem
