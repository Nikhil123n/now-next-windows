# Plan: Context Capsule and restorative Break journey

- Owner: Prompt 6 implementation
- Status: Complete
- Updated: 2026-07-18
- Related decision/issue: [ADR 0004](../decisions/0004-app-owned-sqlite-persistence.md)

## Goal and observable success

Extend the existing Today/Focus vertical slice so a focus boundary can lead through
optional Landing, Done or Park, a bounded count-up Break, and an explicitly confirmed
return. Parking durably saves a minimal Context Capsule and selecting the parked task
shows that context before another focus session can start. Landing, Break, and recovery
remain authoritative across process restart.

## Constraints and assumptions

- Preserve the product invariants in [PRODUCT.md](../../PRODUCT.md), especially the normal
  Focus view's label-and-timer-only contract and the prohibition on silently counting an
  unobserved absence.
- Implement only the approved P0 behavior in
  [FEATURES_FORWARD.md](../../FEATURES_FORWARD.md), while preserving every exclusion and
  evidence gate in
  [FEATURES_DEFERRED_OR_REMOVED.md](../../FEATURES_DEFERRED_OR_REMOVED.md).
- Extend the existing Core state machine, App runtime, and SQLite store; do not create a
  second workflow, project, general repository, health clock, or recommendation engine.
- Landing remains an optional five-minute focus transition. Break is separate count-up
  time, defaults to five minutes, presents one selected approved prompt, and never starts
  another session automatically.
- Explicit abandon is a distinct terminal outcome. It is the only route that permits
  ending without the next physical action required by Park.
- Add migration 3 without rewriting committed migrations. Treat the known successful
  `dotnet format` WinUI workspace diagnostic as informational when its exit code is zero.

## Steps

1. Extend the immutable session model with explicit abandon, bounded Break progress and
   completion, approved Break prompts, and checkpoint shapes that survive restart.
2. Add migration 3 and small parameterized store operations for Context Capsules, Break
   defaults, and atomic Park-plus-capsule persistence through the existing runtime.
3. Extend Today/Focus presentation with restrained boundary, Park, Break, recovery, and
   pre-focus confirmation surfaces; add deterministic tests and update documentation.

## Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1`
- Launch the packaged app and execute the Prompt 6 manual test script for Done, Landing,
  Park, abandon, Break prompts, final return context, explicit confirmation, restart in
  Landing and Break, keyboard access, Reduced Motion, and touch-size checks.
- Confirm both feature-register hashes remain unchanged and migrations 1 through 3 pass
  foreign-key integrity checks.

Completed on Windows build `10.0.26200.8875` x64 with temporary .NET SDK `10.0.302`,
.NET runtime `10.0.10`, MSBuild `18.6.11`, Windows App SDK package `2.3.1`, installed
Windows App Runtime `2.3.1.0` x64, and Visual Studio Build Tools `18.5.1`:

- The canonical command passed repository validation, locked restore, format verification,
  Release build with zero warnings/errors, and all 113 Microsoft.Testing.Platform tests.
  `dotnet format` emitted only the documented successful WinUI workspace diagnostic and
  left source correctly formatted.
- Packaged `dotnet run` launched a responsive `NOW/NEXT` window. Windows UI Automation
  found the Today shell and readiness content, and the process closed cleanly.
- Read-only inspection of the package LocalState database found migrations 1, 2, and 3,
  all three expected current tables, and no `PRAGMA foreign_key_check` rows.
- The deterministic suite covers every state/command pair, Landing and Break recovery,
  boundary idempotence, Park-plus-capsule atomicity, retained parked context, explicit
  abandon, bounded Break completion, migration upgrades/rollback, and the static Focus
  and Break visual contracts. Physical touch, high contrast, text scaling, hardware
  suspend, and the complete human interaction journey remain the documented manual
  Surface checks.

## Risks and rollback

The main risks are checkpoint-shape incompatibility, saving a Park outcome without its
capsule, counting away time after restart, or implicitly starting focus when Break ends.
The migration is additive, Park and capsule writes share one SQLite transaction, runtime
publication follows persistence, and return always stops at an explicit pre-focus choice.
Code can be rolled back while leaving migration 3's additive tables and columns intact;
committed migration history must not be rewritten.
