# Plan: Today planning and focus vertical slice

- Owner: Prompt 5 implementation
- Status: Complete
- Updated: 2026-07-18
- Related decision/issue: [ADR 0004](../decisions/0004-app-owned-sqlite-persistence.md)

## Goal and observable success

Provide a visually plain Windows 11 flow in which the user can maintain today's ordered
tasks, start either authoritative timer mode, operate a focus session by touch or keyboard,
and safely recover a persisted session after restart. Success is demonstrated by the
canonical verification command and the manual packaged-app interaction script.

## Constraints and assumptions

- Preserve the product invariants in [PRODUCT.md](../../PRODUCT.md), including a normal
  Focus view containing only the short focus label and timer.
- Implement only the P0 capabilities authorized by
  [FEATURES_FORWARD.md](../../FEATURES_FORWARD.md); preserve the exclusions and evidence
  gates in [FEATURES_DEFERRED_OR_REMOVED.md](../../FEATURES_DEFERRED_OR_REMOVED.md).
- Reuse the App-owned SQLite store and Core focus-session state machine. The UI projects
  immutable session views and never derives elapsed time from a UI tick counter.
- Keep the existing three-project shape and pinned dependencies. Add no UI framework,
  service layer, view-model framework, theme system, background service, or schema change.
- Treat the known successful `dotnet format` WinUI generated-file workspace diagnostic as
  informational when its exit code is zero and source formatting remains unchanged.

## Steps

1. Add validated Today editing, chronological display, explicit Fixed/Flexible labels,
   reordering, deletion, and a clear Start action using the existing store.
2. Add a Focus surface that renders Core session views, checkpoints active work, supports
   recovery and legal session commands, and provides transient accessible controls.
3. Add deterministic presentation tests and a manual interaction/accessibility script;
   update repository documentation and validation without changing either feature register.

## Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1`
- Launch with the documented packaged `dotnet run` path and complete the scenarios in
  [Prompt 5 manual test script](../testing/prompt-5-manual-test-script.md).
- Confirm count-up/countdown, exact limit and overtime, pause/resume, Landing, extension,
  completion, parking, restart recovery, keyboard/touch reveal, inactivity hiding, stable
  colon layout, and Reduced Motion behavior.

Completed on Windows `10.0.26200` x64 with .NET SDK `10.0.302`, .NET runtime `10.0.10`,
and MSBuild `18.6.11`:

- The canonical command passed locked restore, format verification, Release build with
  zero warnings/errors, and all 95 MTP tests. `dotnet format` emitted only the documented
  successful WinUI workspace warning and changed no source during verification.
- Packaged `dotnet run` launched the responsive `NOW/NEXT` window. Windows UI Automation
  created and restored a task, started count-up Focus at full-screen `1920 × 1080`, found
  only the focus label and authoritative timer in the normal visible tree, revealed the
  temporary controls intentionally, exercised pause/resume and Finish, and removed the
  verification task.
- Three-second positive-duration development tasks exercised both real packaged timer
  modes through their limits. Count-up changed from `0 minutes 0 seconds` to
  `overtime 0 minutes 1 second`; countdown changed from `0 minutes 3 seconds` to positive
  overtime. The visible `+` glyph, Landing start, and five-minute extension also passed.
- Closing a running session committed `RecoveryRequired` and `2.6280229` seconds of active
  time. Relaunch displayed the saved timer plus explicit exclude/include-away recovery
  controls. A confirmed development reset removed only the test database; clean relaunch
  recreated schema migrations 1 and 2 with zero active tasks.
- Physical touch, Reduced Motion, high contrast, text scaling, Narrator, and hardware
  Modern Standby remain the documented manual Surface checks; deterministic tests cover
  their state/timer logic where possible.

## Risks and rollback

The main risks are UI commands racing a refresh/checkpoint, recovery prompts obscuring an
unresolved session, or XAML layout drift introducing permanent focus controls. Operations
are serialized by the existing runtime, session writes remain persist-before-publish, and
the Focus visual tree keeps transient controls in a separate collapsed overlay. No migration
is added; rollback is the code and documentation change only, with existing databases
remaining compatible.
