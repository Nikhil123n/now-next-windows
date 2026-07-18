# 0004 — App-owned SQLite persistence

- Status: Accepted
- Date: 2026-07-18

## Context

[ADR 0001](0001-windows-native-project-shape.md) assigned persistence to Core before a
concrete database caller existed. Prompt 3 needs the package-identity-aware Windows
application-data path, SQLite migrations, and startup initialization, while Core needs
only deterministic Today-planning behavior. Putting Windows storage and SQLite in Core
would make that assembly platform-aware without improving the current design.

## Decision

Keep the existing Windows-only three-project shape. `NowNext.Core` owns the immutable
Today domain and validation and remains dependency-free. `NowNext.App` owns the concrete
SQLite store, schema migrations, per-user `ApplicationData` path, and launch-time
composition. No repository interface is added because there is one concrete caller.

`NowNext.Core.Tests` targets the App's Windows TFM and references both production
projects so the one test assembly can cover domain and persistence behavior. Windows App
SDK deployment auto-initialization is disabled for that unpackaged test host and for the
packaged App assembly; the package manifest's framework dependency provides the runtime
when the application launches with package identity.

## Consequences

SQLite remains visible and product-specific. App depends on Core, but Core does not
depend on App or Windows APIs. Deleted task rows can remain stable future reference
targets without introducing session history now. The storage component should move to a
separate project only if a second executable needs it or it becomes independently
reusable; either change requires a new decision and must stay within the three-project
contract unless that contract is explicitly revised.

## Supersedes

[ADR 0001](0001-windows-native-project-shape.md), only for persistence ownership and the
test-project reference boundary. Its Windows-native project limits remain in force.
