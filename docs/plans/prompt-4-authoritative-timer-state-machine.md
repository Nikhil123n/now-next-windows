# Plan: Authoritative timer and session state machine

- Owner: Prompt 4 implementation
- Status: Complete
- Updated: 2026-07-18
- Related decision/issue: [ADR 0002](../decisions/0002-authoritative-time-and-recovery.md)

## Goal and observable success

Implement a pure, testable focus-session state machine in Core and the smallest App-owned
durable-checkpoint integration. Count-up and countdown must remain correct without UI
refreshes, impossible state combinations must be unrepresentable, illegal commands must
fail clearly, and restart recovery must never invent focused time.

Success is demonstrated by deterministic state/command, timing, property-oriented,
migration, and persistence tests; the canonical verification command; packaged readiness
shell launch; and a Windows suspend/recovery check when the environment permits it.

## Constraints and assumptions

- Preserve [the approved feature register](../../FEATURES_FORWARD.md) and
  [the deferred/removed register](../../FEATURES_DEFERRED_OR_REMOVED.md) byte-for-byte.
- Follow [ADR 0002](../decisions/0002-authoritative-time-and-recovery.md): Core owns
  authoritative time and recovery, active elapsed time is monotonic, and persisted UTC
  timestamps never silently turn an unobserved interval into focus.
- Keep only App, Core, and the single MSTest project; add no package or generic storage
  abstraction.
- Prefer immutable domain state and pure transitions. Use `async`/`await` for SQLite I/O
  and one narrow built-in asynchronous gate only where App events must be serialized.
- Preserve migration `0001`; add ordered migration `0002` for one current-session
  checkpoint. Do not add a session-history table.
- Keep the packaged shell plain. Do not add polished UI, task screens, background
  services, adaptive/additional focus modes, AI, schedule repair, or deferred features.

## Steps

1. Add the immutable Core session types, legal command/state transitions, monotonic timer
   projections, limit/overtime/Landing rules, and validated durable checkpoint model.
2. Extend the App-owned SQLite store with ordered schema version 2 and atomic
   checkpoint/task-state operations; add the narrow runtime and Windows lifecycle bridge.
3. Add exhaustive deterministic tests, property-oriented transition sequences, migration
   and restart tests; update repository policy and documentation without changing either
   feature register.

## Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1`
- Require locked restore, formatting exit code zero, Release build with zero compiler
  warnings, and all Microsoft.Testing.Platform tests passing.
- Treat the known WinUI duplicate generated-file `dotnet format` diagnostic as
  informational only when formatting exits zero and leaves source correctly formatted.
- Launch the packaged app and confirm the readiness shell and schema version 2.
- Simulate save/restart with a fresh monotonic origin and verify recovery excludes the
  unobserved interval unless a bounded duration is explicitly included.
- Attempt a Windows suspend or Modern Standby cycle. Record it as a remaining manual
  integration check if the environment cannot automate it reliably.

Verification completed on Windows 11 build `10.0.26200` with the temporary pinned .NET
SDK `10.0.302` installation:

- the canonical command passed locked restore and formatting verification; `dotnet
  format` emitted only the documented non-failing WinUI generated-workspace diagnostic;
- the Release solution build completed with zero warnings and zero errors;
- all 79 Microsoft.Testing.Platform tests passed, including the exhaustive transition
  matrix, fixed-seed sequences, migration upgrade/rollback, persistence restart, and App
  lifecycle integration cases;
- packaged `dotnet run` exposed a `NOW/NEXT` window whose UI Automation tree contained
  `NOW/NEXT prototype is ready.`; and
- the package LocalState database contained migrations 1 and 2 plus the
  `current_session_checkpoint` table after launch.

A real hardware suspend/Modern Standby cycle was not forced because doing so would
disrupt the active development session. The deterministic interruption/resume tests and
the packaged Windows power-event wiring passed their automated checks; hardware standby
remains the one manual integration check.

## Risks and rollback

Boundary double-counting, reuse of a process-local timestamp, or inferring focus from UTC
could invent time. Immutable transitions, idempotent boundary detection, durable committed
durations, and fake-clock restart tests contain that risk. Concurrent UI/power events
could publish state before it commits; the App serializes and persists each transition
before reporting success.

Migration `0002` is append-only once merged. Before merge, rollback removes Prompt 4 code
and the uncommitted migration. After merge, any schema correction requires a new forward
migration; migration `0001` and retained task rows are never rewritten or deleted.
