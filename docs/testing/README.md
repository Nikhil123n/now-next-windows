# Testing and Verification

Testing protects honest timekeeping, deliberate transitions, and local recovery. Prefer
deterministic Core tests over timing sleeps or UI-driven business logic.

## Prompt 1

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-Repository.ps1
```

The repository validator invokes the audited-skill validator, checks required knowledge
and policy files, feature-register hashes and references, internal Markdown links, CI
policy, and the absence of product implementation.

## Prompt 2 and later

CI and local verification must run:

```powershell
dotnet restore .\NowNext.slnx --locked-mode
dotnet format .\NowNext.slnx --verify-no-changes --no-restore
dotnet build .\NowNext.slnx --configuration Release --no-restore -warnaserror
dotnet test --solution .\NowNext.slnx --configuration Release --no-build --results-directory .\TestResults --report-trx
```

The .NET 10 test command uses Microsoft.Testing.Platform syntax. Prompt 2 must make all
four commands mandatory in CI and retire the Prompt 1 implementation-absence check.

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
