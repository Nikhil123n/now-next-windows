# Architecture

This architecture serves the product defined in [FEATURES_FORWARD.md](FEATURES_FORWARD.md)
and respects every exclusion and evidence gate in
[FEATURES_DEFERRED_OR_REMOVED.md](FEATURES_DEFERRED_OR_REMOVED.md). Those files remain
authoritative when this overview is incomplete.

## Approved stack

- C# 14 on .NET 10 LTS SDK `10.0.302`.
- WinUI 3 on Windows App SDK `2.3.1`.
- `Microsoft.Data.Sqlite` `10.0.10` with direct parameterized SQL.
- `CommunityToolkit.Mvvm` `8.4.2` only where it removes real view-model boilerplate.
- MSTest `4.3.2` on Microsoft.Testing.Platform.

Versions are centrally pinned for the future projects. Adding or upgrading a dependency
requires a current feature need, license review, Windows/.NET compatibility review, and
passing verification. Do not add a package for logic that is clear in the platform or
standard library.

## Project boundaries for Prompt 2

- `NowNext.Core`: deterministic domain state transitions, timing calculations, schedule
  repair, recovery decisions, SQLite persistence, and migrations. It has no WinUI types.
- `NowNext.App`: WinUI views, view models, accessibility, Windows lifecycle integration,
  Reduced Motion detection, notifications/cues, and the explicit composition root.
- `NowNext.Core.Tests`: MSTest coverage for Core behavior and persistence. UI-specific
  verification remains separate and Windows-only.

There are at most two production projects and one test project. Use explicit constructor
composition or built-in .NET facilities. Do not introduce a generic repository, mediator,
AutoMapper, Entity Framework, WebView, external dependency-injection framework, or
future-platform adapter.

## State, timing, and recovery

Core owns the authoritative session state machine. UI events request transitions; views
do not calculate or persist authoritative elapsed time. Use `TimeProvider` so time can be
controlled in tests. Persist committed active duration and recovery checkpoints rather
than trusting a continuously running UI timer.

On restart, crash, sleep, resume, or long absence, present an explicit recovery choice.
Unobserved time is excluded unless the user deliberately includes it. Count-up and
countdown share transition rules and both enter positive overtime after their limit.

Schedule repair is a pure, deterministic proposal before persistence: preserve Fixed
items and shutdown, consume buffer, move Flexible work, then suggest a deferral if
needed. Applying the proposal is a separate, transactional, user-approved operation.

## Persistence and migrations

Use a concrete SQLite store organized around product operations, not a generic storage
interface. Execute parameterized SQL inside explicit transactions. Commit recovery-
critical data before reporting success to the UI.

Maintain an ordered schema-migration table. Once a migration reaches `main`, never edit,
renumber, or reuse it; add a forward migration. Migration application is transactional
where SQLite permits it. Back up or fail clearly before any migration that cannot be made
safely reversible. Tests cover a fresh database and upgrades from every retained schema
baseline.

## Accessibility and diagnostics

Support keyboard and touch, semantic labels, focus order, high contrast, text scaling,
and Windows Reduced Motion. The blinking colon must not shift layout and becomes static
when reduced motion applies. Diagnostic logs are local and exclude task content unless
the user knowingly exports it.
