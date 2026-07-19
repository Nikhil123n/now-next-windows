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
- Task-editor validation, timer-text formatting from authoritative `SessionView` values,
  legal focus-control policy, and Focus/Break recovery affordances.
- A static WinUI contract check that normal Focus content contains the label and segmented
  timer, keeps controls/recovery overlays collapsed, has no progress bar, separates the
  colon glyph so blinking cannot move layout, and gives focus controls 44-pixel minimum
  touch height.
- A static Break contract check that the surface starts collapsed, renders exactly one
  prompt, delays return context until the final portion, has no progress bar, and uses
  44-pixel minimum action targets.

SQLite tests use unique temporary database paths and never access the packaged user's
LocalState. WinUI controls are not instantiated in the MTP test process because the
repository deliberately has no fourth UI-test project or qualified Visual Studio WinUI
test harness.

## Windows UI verification

Run the [Prompt 6 manual test script](prompt-6-manual-test-script.md) on Windows 11 for
packaged launch, CRUD/reordering, full-screen layout, keyboard and touch paths, transient
controls, exact user-visible timer behavior, Landing/Park/Break/return, restart recovery,
high contrast, text scaling, and Reduced Motion. The
[Prompt 5 script](prompt-5-manual-test-script.md) remains the baseline Today/Focus check.
Hardware suspend/Modern Standby remains a manual integration check and does not justify a
background service.

Later P0 milestones must add deterministic schedule repair, Fixed/shutdown protection,
approval/undo, and general history. Prototype
success still requires 10–14 real working days without lost state, invented time, silent
Fixed movement, or cloud availability.
