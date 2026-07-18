# Testing and Verification

Testing protects honest timekeeping, deliberate transitions, and local recovery. Prefer
deterministic Core tests over timing sleeps or UI-driven business logic.

## Prompt 3 and later

CI and local verification use the same canonical command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1
```

The script validates repository policy, performs the locked restore, verifies formatting,
builds Release, and runs tests. The .NET 10 test command uses
Microsoft.Testing.Platform syntax.

The suite retains the Core assembly smoke test and adds deterministic domain and SQLite
coverage. Persistence tests use a fixed `TimeProvider`, unique temporary database paths,
and never access the packaged user's LocalState. They verify exact round trips, ordering,
mutation rollback, soft deletion, cancellation, corrupt data, migration versioning and
rollback, and foreign-key integrity.

## Required automated coverage

- Count-up and countdown start, pause, resume, limit, and positive-overtime behavior.
- Every legal and illegal focus, Limit Reached, Landing, Park, Break, recovery, and
  shutdown transition.
- `TimeProvider` boundary cases without real waiting.
- Sleep, suspension, crash, restart, and long-absence recovery choices; away time is
  excluded by default.
- Deterministic repair order, Fixed and shutdown protection, approval, and undo.
- Context Capsule requirements and next-action restoration.
- Transactional SQLite operations, fresh schema creation, retained-version upgrades,
  failure rollback, and recovery checkpoints.

## Windows UI verification

On supported Windows 11 hardware, verify touch and keyboard paths, focus order, semantic
labels, text scaling, high contrast, and Reduced Motion. The active view normally contains
only the focus label and timer. Confirm the colon blinks without shifting layout under
normal motion settings and remains static when Reduced Motion applies.

## Manual prototype acceptance

Exercise the complete journey in the approved feature register, including restart and
sleep during active focus, Landing, and Break. The prototype success gate is 10–14 real
working days without lost state, invented time, silent Fixed-task movement, or cloud
availability.
