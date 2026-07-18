# Plan: Today domain model and SQLite storage

- Owner: Prompt 3 implementation
- Status: Complete
- Updated: 2026-07-18
- Related decision/issue: [ADR 0004](../decisions/0004-app-owned-sqlite-persistence.md)

## Goal and observable success

Persist and restore the current local day's ordered tasks with exact domain values,
versioned migrations, safe soft deletion, and no task UI or timer behavior. Success is
demonstrated by the canonical verification command, packaged launch, database inspection,
guarded reset, and migration recreation.

## Constraints and assumptions

- Preserve [the approved feature register](../../FEATURES_FORWARD.md) and
  [the deferred/removed register](../../FEATURES_DEFERRED_OR_REMOVED.md) byte-for-byte.
- Keep only App, Core, and the single MSTest project.
- Use direct parameterized SQLite, explicit transactions, and explicit migrations; no
  ORM, generic repository, service layer, or speculative extension point.
- Model only today's plan. Do not add task screens, timers, breaks, repair, recurrence,
  categories, dependencies, rich notes, AI, calendar integration, or backlog behavior.
- Use the pinned SQLitePCLRaw 3.0.3 bundle because the dependency selected by
  Microsoft.Data.Sqlite 10.0.10 contains a high-severity native-library advisory.

## Steps

1. Add immutable domain types and validation to dependency-free Core.
2. Add App-owned schema version 1, concrete Today operations, startup initialization,
   and a guarded repository-only database tool.
3. Add deterministic domain, persistence, corruption, migration, cancellation, and
   foreign-key tests; update contracts and dependency locks.

## Verification

- `dotnet restore .\NowNext.slnx --force-evaluate` once to regenerate lock files.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1`.
- Packaged `dotnet run`, UI Automation readiness text, and LocalState database creation.
- `scripts\Database-Dev.ps1 -Inspect`; rejected unconfirmed/running-app reset; confirmed
  reset after close; relaunch and migration recreation.

The known non-failing WinUI generated-file workspace diagnostic from `dotnet format` is
informational only when the process exits zero and source remains formatted.

Completed on 2026-07-18:

- The unlocked restore regenerated all three lock files. The final canonical command
  passed repository validation, locked restore, formatting verification, Release build
  with zero warnings/errors, and all 31 MTP tests with no skips.
- Formatting exited zero and emitted only the known WinUI workspace warning; a subsequent
  verification reported no source formatting changes.
- Packaged `dotnet run` opened the responsive `NOW/NEXT` window. Windows UI Automation
  found the enabled `NOW/NEXT prototype is ready.` text after launch-time migration.
- Launch created `now-next.db` in the prototype package LocalState directory. Inspection
  reported its exact path and metadata; unconfirmed reset and running-app reset were
  rejected; confirmed reset removed the one 40,960-byte database; relaunch recreated it.
- Verification used Windows 11 `10.0.26200` x64, Developer Mode, Windows SDK
  `10.0.26100.0`, Windows App Runtime `2.3.1.0` x64, temporary .NET SDK `10.0.302`,
  .NET runtime `10.0.10`, MSBuild `18.6.11.33009`, dotnet-format
  `10.0.302-servicing.26329.109+35b593bebfcba58f8e78298cef14c2761f5d86c6`,
  PowerShell `5.1.26100.8875`, and Visual Studio Build Tools 2026 `18.5.1`.

## Risks and rollback

Migration or path mistakes could make local state unavailable. Versioned migration tests,
transaction rollback, fixed LocalState resolution, and the reset script's exact-path
guards contain that risk. Before version 1 reaches `main`, rollback is the Prompt 2
scaffold plus deletion of the prototype database; after merge, schema changes require a
forward migration.
