# Plan: Deterministic schedule repair, Recovery Mode, and Shutdown

- Owner: Prompt 7 implementation
- Status: Complete
- Updated: 2026-07-18
- Related decision/issue: [ADR 0005](../decisions/0005-deterministic-repair-and-day-closure.md)

## Goal and observable success

Extend the existing Today/Focus/Break vertical slice with one deterministic, explained
same-day repair proposal; a calm Recovery decision surface; and durable, explicitly
confirmed Shutdown. Fixed commitments and the configured shutdown remain protected,
repair never applies without acceptance, the latest accepted repair can be safely
undone, and a closed day remains closed after restart.

## Constraints and assumptions

- Preserve the invariants in [PRODUCT.md](../../PRODUCT.md) and implement only approved
  capabilities from [FEATURES_FORWARD.md](../../FEATURES_FORWARD.md), without weakening
  [FEATURES_DEFERRED_OR_REMOVED.md](../../FEATURES_DEFERRED_OR_REMOVED.md).
- Extend the existing Core engine, App runtime, and App-owned SQLite store. Add no
  project, package, optimizer, background service, generic repository, or alternate
  workflow.
- Operate only on today's Fixed and Flexible tasks. Fixed intervals and shutdown never
  move; a proposal has at most one deferral and is non-acceptable when one deferral
  cannot make the day fit.
- Use local offsets from midnight for schedule arithmetic, monotonic observations for
  foreground absence detection, and UTC only for durable event metadata.
- Preserve migrations 1 through 3 and add migration 4. Leave both authoritative feature
  registers byte-for-byte unchanged.

## Steps

1. Add the pure repair engine and workday projections with deterministic rule-order,
   impossible-result, recovery, and Shutdown tests.
2. Add migration 4 and transactional App-store operations for settings, revisions,
   session ledger totals, accepted repair/undo, and durable closure.
3. Extend the existing runtime and plain WinUI surfaces, add static and manual
   interaction coverage, update architecture/schema/invariant documentation, and run
   the canonical verification and packaged launch checks.

## Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1`
- Launch the packaged app and execute the
  [Prompt 7 manual test script](../testing/prompt-7-manual-test-script.md).
- Confirm migrations 0 through 3 upgrade to 4, foreign-key checks are empty, the feature
  register hashes remain unchanged, and the Release build has zero warnings.
- Confirm a 15-minute missed foreground interval restores only durable committed time,
  repair accept/undo is transactional, and keep-awake release follows durable closure.

Completed on Windows 11 Home `10.0.26200` x64 with temporary .NET SDK `10.0.302`,
.NET runtime `10.0.10`, MSBuild `18.6.11.33009`, Windows SDK `10.0.26100`, Windows App
SDK package `2.3.1`, installed Windows App Runtime `2.3.1.0` x64, and Visual Studio Build
Tools 2026 `18.5.1`:

- The canonical command passed repository validation, locked restore, formatting
  verification, Release build with zero warnings/errors, and all 145
  Microsoft.Testing.Platform tests. `dotnet format` emitted only the documented
  successful WinUI workspace diagnostic and changed no source.
- Packaged `dotnet run` opened a responsive `NOW/NEXT` window. Windows UI Automation
  found Today, Add task, and the explicit protected-shutdown control, after which the
  app closed normally.
- Read-only LocalState inspection found migration version 4 with four contiguous
  migration records, all seven Prompt 7 tables, and zero `PRAGMA foreign_key_check`
  rows. Both authoritative feature-register hashes remain unchanged.
- Automated coverage verifies deterministic repair/property sequences, protected
  conflicts and impossible results, transactional accept/cancel/undo and stale guards,
  migration upgrades/backfill/constraints, exact committed-time recovery, Shutdown
  projections/retention, post-closure rejection, and keep-awake ordering/failure.
- Physical touch, High Contrast, 200% text scaling, hardware suspension/Modern Standby,
  and the complete stateful human journey remain the documented manual Surface checks.
  Visual Studio's WinUI XAML/MSIX designers and debugger integration remain unqualified;
  the verified development path is the .NET CLI workflow.

## Risks and rollback

The principal risks are moving protected time, applying a stale proposal, overwriting
later task edits during undo, inventing focused time after an absence, or reporting
Shutdown before its transaction commits. Core proposals are pure; apply checks both the
base revision and protected shutdown; undo checks every affected persisted value; and
runtime publication/release follows commit. Code can be rolled back while retaining the
additive migration-4 tables and column. Committed migration history must not be edited.
