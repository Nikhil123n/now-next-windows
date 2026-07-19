# Testing and Verification

Testing protects honest timekeeping, deliberate transitions, local recovery, and the
minimal Focus presentation. Prefer deterministic Core and presentation tests over timing
sleeps or UI-driven business logic.

## Canonical verification

CI and local verification use the same command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1
```

The script validates repository policy, performs the locked restore, verifies formatting,
builds Release with warnings as errors, and runs tests through Microsoft.Testing.Platform.
The known WinUI generated-file workspace diagnostic is informational only when
`dotnet format` exits zero and leaves source correctly formatted.

## Automated coverage

- Today-domain validation, ordering, exact SQLite round trips, mutation rollback, soft
  deletion, cancellation, corrupt data, migration upgrades, and foreign-key integrity.
- Count-up and countdown projections at, before, and beyond the limit; positive overtime;
  the full state/command matrix; pause/resume; Landing; extension; terminal outcomes;
  bounded Break completion; explicit abandonment; and confirmed return states.
- Fixed-seed transition sequences, restart with a new monotonic origin, UTC changes,
  suspension/recovery, and bounded explicit inclusion of away time without timing sleeps.
- Atomic Park-plus-Context-Capsule storage, capsule retention, Break defaults, version 3
  migration upgrades/rollback, Break restart, and corrupt-data handling.
- Deterministic buffer use, Flexible movement across Fixed anchors, one-task deferral,
  impossible repair results, fixed-seed Fixed/shutdown protection, stale acceptance,
  transactional apply/cancellation/undo, and stale undo rejection.
- Version 4 upgrades from every retained baseline, checkpoint-ledger backfill, schedule
  revision constraints, retained multi-session totals, durable closure, post-closure
  mutation rejection, and keep-awake release ordering/failure behavior.
- Recovery next-Fixed/available-time projections, the exact 15-minute substantial-
  absence contract, durable-time reload under UTC changes, Shutdown ranking, Daily Win
  states, and Landing-in/Break-out totals.
- Task-editor validation, timer-text formatting from authoritative `SessionView` values,
  legal focus-control policy, and Focus/Break recovery affordances.
- A static WinUI contract check that normal Focus content contains the label and segmented
  timer, keeps controls/recovery overlays collapsed, has no progress bar, separates the
  colon glyph so blinking cannot move layout, and gives focus controls 44-pixel minimum
  touch height.
- A static Break contract check that the surface starts collapsed, renders exactly one
  prompt, delays return context until the final portion, has no progress bar, and uses
  44-pixel minimum action targets.
- A static Prompt 7 contract check that Today exposes one calm proposal, Recovery choices
  appear in deliberate order with accessible 44-pixel targets, Shutdown defaults to no
  mutation until confirmation, and no red overdue-task wall is present.
- App-level fake Windows services verify display-request acquire/release boundaries,
  serialized suspend/resume and exit checkpoints, Recovery routing without away time,
  startup/settings reset behavior, and controlled diagnostic fields without sleeps.
- SQLite online backup/export/restore/reset tests verify exact round trips, independent
  loadable copies, corruption rejection without live-data loss, pre-cancellation, and
  package-local cleanup. Static WinUI/manifest checks require accessible touch targets,
  explicit restore/reset confirmation, and a startup task disabled by default.
- Release-candidate suites run the persisted Today-to-Shutdown journey through both timer
  modes; restore every active session phase; exercise practical child-process termination;
  advance fake monotonic time through long sessions and repeated idempotent boundaries;
  upgrade from schema baselines 0, 1, 2, 3, and 4; verify keyboard command and Reduced
  Motion policies; and restore task, capsule, setting, and committed checkpoint data from
  validated backups.

SQLite tests use unique temporary database paths and never access the packaged user's
LocalState. WinUI controls are not instantiated in the MTP test process because the
repository deliberately has no fourth UI-test project or qualified Visual Studio WinUI
test harness.

## Windows UI verification

Run the [Prompt 8 Surface hardware test](prompt-8-surface-hardware-test.md) on the target
device for touch, display idle/wake, explicit sleep, lid-close, Modern Standby resume,
autostart, battery, long-running focus, and local backup/restore/reset behavior. Run the
[Prompt 7 manual test script](prompt-7-manual-test-script.md) on Windows 11 for
repair review/accept/undo, protected Fixed/shutdown changes, late Recovery, explicit
outcomes, close-early, durable Shutdown, and the resting state. Run the
[Prompt 6 manual test script](prompt-6-manual-test-script.md) for
packaged launch, CRUD/reordering, full-screen layout, keyboard and touch paths, transient
controls, exact user-visible timer behavior, Landing/Park/Break/return, restart recovery,
high contrast, text scaling, and Reduced Motion. The
[Prompt 5 script](prompt-5-manual-test-script.md) remains the baseline Today/Focus check.
Hardware suspend/Modern Standby remains a manual integration check and does not justify a
background service.

Run the [Prompt 9 release-candidate test](prompt-9-release-candidate-test.md) from a clean
package installation for the unified P0 journey, restart/termination matrix, offline
launch, keyboard/touch/Reduced Motion checks, backup/restore/uninstall, and raw cold-start,
idle, and long-run measurements. Record unrun hardware cases as limitations rather than
passes.

Later milestones may expose deliberately approved general history without changing these
repair/recovery or Windows power invariants. Prototype success still
requires 10–14 real working days without lost state, invented time, silent Fixed
movement, or cloud availability.
