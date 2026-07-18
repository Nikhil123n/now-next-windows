# 0001 — Windows-native project shape

- Status: Superseded by [0004 — App-owned SQLite persistence](0004-app-owned-sqlite-persistence.md)
- Date: 2026-07-18

## Context

NOW/NEXT is for one user on one Windows 11 Surface. Cross-platform and distributed
designs are expressly removed from the product direction.

## Decision

Use C# 14, .NET 10 LTS, WinUI 3, and the Windows App SDK. Prompt 2 may create only
`NowNext.App`, `NowNext.Core`, and `NowNext.Core.Tests`. App owns Windows UI and lifecycle;
Core owns deterministic behavior and persistence; the MSTest project verifies Core.
Compose dependencies explicitly without an external container.

## Consequences

Windows APIs may be used directly in App. Core remains free of WinUI types for testability,
not for hypothetical portability. No web, mobile, cross-platform, backend, mediator,
mapping, or generic repository layer is permitted.

## Supersedes

None.
