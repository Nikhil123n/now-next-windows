# Plan: Dependable Windows integration and local data safety

- Owner: Codex
- Status: Complete
- Updated: 2026-07-18
- Related decision/issue: [ADR 0006](../decisions/0006-windows-lifecycle-and-local-data-safety.md)

## Goal and observable success

Make the existing one-Surface vertical slice dependable across idle display timeout,
sleep/resume, app exit, restart, and local database maintenance. The app must commit a
recoverable checkpoint before observed suspension or close, restore through Recovery
Mode, bound display keep-awake to user-enabled active sessions, support packaged
launch-at-sign-in and optional full-screen startup, and provide validated local
backup/export/restore/reset with diagnostics that never record user task content.

## Constraints and assumptions

- Preserve every invariant in [the approved feature register](../../FEATURES_FORWARD.md)
  and every exclusion in
  [the deferred/removed register](../../FEATURES_DEFERRED_OR_REMOVED.md).
- Keep Windows behavior in `NowNext.App`; do not add a project, package, cross-platform
  adapter, background service, updater, telemetry, or remote reporting.
- A Windows display request may prevent idle display-off only while the user setting is
  enabled and focus, overtime, Landing, or Break is actively accruing. It is released on
  pause, recovery, completion, day close, suspension, and process exit and never attempts
  to override explicit sleep, lid-close, power, or shutdown actions.
- SQLite maintenance uses online SQLite backup semantics and validates integrity,
  foreign keys, and the known migration sequence before a backup is accepted or a
  restore replaces live data.
- Default diagnostic entries contain only controlled event/result identifiers, UTC
  timestamps, and exception type names; arbitrary messages and task data are not an
  input to the logger.

## Steps

1. Add narrow Windows path, settings, power-event, Reduced Motion, startup-task, and
   display-request services plus a serialized lifecycle coordinator.
2. Add privacy-safe local diagnostics and database backup/export/restore/reset with
   confirmation, integrity validation, safe path handling, and rollback on failed
   restore.
3. Wire compact Today settings/data controls, packaged startup-task metadata, lifecycle
   recovery notification, and optional full-screen startup without changing Focus
   content.
4. Add deterministic MSTest fakes and coverage, a Surface hardware manual test script,
   and update architecture, scope/status, testing, documentation, and repository checks.

## Verification

- Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1`.
- Launch the packaged app, exercise settings and a local backup, and confirm the
  readiness/Today surface and database remain usable.
- On the target Surface, complete the Prompt 8 manual script for touch, battery, display
  wake, explicit sleep, lid close, resume Recovery, autostart, restart, full-screen
  startup, backup/restore/reset safeguards, and a long-running session.
- Treat the known WinUI `dotnet format` generated-file workspace diagnostic as
  informational only when the process exits zero and source remains formatted.

Completed on Windows 11 Home build `10.0.26200` x64 with temporary .NET SDK
`10.0.302`, .NET runtime `10.0.10`, MSBuild `18.6.11.33009`, Windows SDK
`10.0.26100`, Windows App SDK package `2.3.1`, installed Windows App Runtime
`2.3.1.0` x64, and Visual Studio Build Tools 2026 `18.5.1`:

- The canonical command passed repository validation, locked restore, formatting
  verification, Release build with zero warnings, and all `158` MTP/MSTest tests.
- `dotnet format` emitted only the documented successful WinUI workspace diagnostic and
  left source formatted.
- Packaged `dotnet run` opened the `NOW/NEXT` window, exposed the readiness text and
  Windows/data controls through UI Automation, and used the package LocalState database.
- Invoking the packaged Backup action produced and reported one validated local backup.
- Automated tests used fake Windows services and isolated databases to verify bounded
  keep-awake, suspend/resume/exit recovery, log privacy, exact backup/restore, corrupt
  restore rejection, export, reset, and cancellation.
- Touch, explicit sleep, Modern Standby, lid-close, sign-in restart, battery, and a
  long-running session remain the non-automatable target-Surface checks in the Prompt 8
  hardware script. Visual Studio's XAML/MSIX designers and debugger remain unqualified;
  the documented .NET CLI path is the verified development path.

## Risks and rollback

The main risks are a display request surviving beyond active work, overlapping lifecycle
callbacks, or replacement of the live database with a corrupt/incompatible file.
Idempotent acquire/release, one asynchronous lifecycle gate, pre/post integrity checks,
and a pre-restore rollback image bound those failures. Code can be rolled back without a
schema downgrade because this prompt adds no migration; user-created backups remain
ordinary validated SQLite files.
