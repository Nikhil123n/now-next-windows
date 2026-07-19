# 0006 — Windows lifecycle and local data safety

- Status: Accepted
- Date: 2026-07-18

## Context

The authoritative session engine and SQLite checkpoint already recover from process
restart, but the prototype still wires power notifications directly in `App`, has a
no-op keep-awake hook, and lacks user-controlled Windows startup and local data
maintenance. These behaviors are Windows-specific and correctness-sensitive. Moving
them into Core or introducing platform-neutral adapters would contradict the one-Surface
product boundary.

## Decision

Keep the integration in `NowNext.App` behind narrow interfaces only where deterministic
tests need fake Windows behavior. A small lifecycle coordinator serializes suspend and
resume notifications and calls the existing `FocusSessionRuntime`; Core timing remains
unchanged. A `Windows.System.Display.DisplayRequest` is idempotently active only for
user-enabled, actively accruing focus/Landing/Break states. It is never a system-required
power request and is released before suspension, during recovery, on day close, and on
exit.

Use package-local `ApplicationData` for settings, diagnostics, backups, and the live
database. Declare one disabled-by-default packaged startup task and respect Windows
states that prevent the app from overriding the user's Task Manager or policy choice.
Read Reduced Motion from `UISettings` and apply optional full-screen startup through the
existing `AppWindow` only after the user enables it.

Use SQLite's online backup operation for backup and export. Validate SQLite integrity,
foreign keys, and the exact known migration sequence before accepting a backup or
restore. Preserve a validated rollback image while replacing the live database. A
complete reset creates a fresh migrated database and clears only files and settings
under the exact package LocalState root after explicit confirmation.

Diagnostics accept controlled event and result identifiers rather than arbitrary text.
They may record an exception type, never its message, and therefore exclude task titles,
notes, Context Capsules, paths selected by the user, and other user-authored content by
construction.

## Consequences

The App gains a few concrete Windows services and deterministic fakes, while Core and the
three-project shape remain unchanged. Display keep-awake cannot and should not make the
timer continue through sleep; the durable checkpoint remains authoritative. Restore and
reset temporarily interrupt an active session into Recovery Mode so no committed time is
invented. Hardware-specific lid, Modern Standby, and sign-in behavior still requires the
documented Surface manual test.

## Supersedes

This decision replaces ADR 0005's temporary no-op keep-awake implementation. ADR 0005's
repair, closure, and release-after-durable-closure decisions remain in force.
