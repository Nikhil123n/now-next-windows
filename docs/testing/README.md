# Testing and Verification

Testing protects honest timekeeping, deliberate transitions, and local recovery. Prefer
deterministic Core tests over timing sleeps or UI-driven business logic.

## Prompt 4 and later

CI and local verification use the same canonical command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1
```

The script validates repository policy, performs the locked restore, verifies formatting,
builds Release, and runs tests. The .NET 10 test command uses
Microsoft.Testing.Platform syntax.

The suite retains the Core assembly, Today-domain, and SQLite coverage and adds exhaustive
deterministic session tests. A fake `TimeProvider` controls monotonic and UTC observations;
tests use no timing sleeps. Persistence tests use unique temporary database paths and
never access the packaged user's LocalState. They verify exact round trips, ordering,
mutation rollback, soft deletion, cancellation, corrupt data, migration versioning and
rollback, foreign-key integrity, and current-session checkpoint recovery.

## Prompt 4 automated coverage

- Count-up and countdown projections at zero, immediately before, exactly at, and beyond
  the approved limit, including positive overtime.
- The full state/command matrix for Ready, Focusing, Paused, LimitReached, Overtime,
  Landing, Break, Completed, Parked, RecoveryRequired, and DayClosed.
- Delayed and partitioned refreshes, repeated boundary checks, pause/resume segments,
  extensions, and the exact five-minute Landing boundary without real waiting.
- Fixed-seed property-oriented command sequences that preserve identity, nonnegative
  durations, terminal stability, and monotonic committed focus.
- Restart under a new monotonic timestamp origin and forward/backward UTC changes.
- Sleep, suspension, crash, restart, and long-absence recovery choices; away time is
  excluded by default and included only by an explicit bounded choice.
- Transactional SQLite operations, fresh schema creation, version 1 to version 2 upgrade,
  failure rollback, checkpoint round trips, and atomic task/session updates.

Later P0 milestones must add deterministic repair order, Fixed/shutdown protection,
approval and undo, plus the complete Context Capsule and next-action restoration flow.
Those are not Prompt 4 verification requirements.

## Windows UI verification

Prompt 4 does not add task or timer UI. On supported Windows 11 hardware, launch the
packaged readiness shell, confirm storage initialization, and exercise a real suspend or
Modern Standby cycle if the environment permits it. The later focus UI must verify touch
and keyboard paths, focus order, semantic labels, text scaling, high contrast, and Reduced
Motion; colon animation is not an acceptance check until that UI exists.

## Manual prototype acceptance

For Prompt 4, the deterministic harness exercises start, interruption, restart, recovery
excluding away time, and explicit bounded inclusion for both timing modes. A real
hardware suspend/Modern Standby cycle remains a manual Windows integration check; it does
not justify adding a background service. The complete product journey remains a later
acceptance gate. Prototype success still requires 10–14 real working days without lost
state, invented time, silent Fixed-task movement, or cloud availability.
