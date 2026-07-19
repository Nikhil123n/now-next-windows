# Timer and Session Invariants

This document defines the authoritative timing and recovery contract through Prompt 6.
It is a behavioral companion to
[ADR 0002](decisions/0002-authoritative-time-and-recovery.md) and does not authorize
unrelated timer, health, or automation features.

## Sources of time

- While a process is running, elapsed duration comes only from the monotonic
  `TimeProvider.GetTimestamp()`/`GetElapsedTime()` pair.
- UTC wall time records persistence and schedule presentation. It is never the source of
  live elapsed duration.
- A UI refresh asks for a projection. It does not add a tick. Delayed, skipped, or
  repeated rendering cannot change authoritative elapsed time.
- Paused, limit-decision, Break-completed, completed, parked, abandoned,
  recovery-required, and day-closed states do not accrue time.
- Break elapsed is separate and never contributes to focused time.

For a running focus or Landing segment with committed active duration `C`, monotonic
anchor `M0`, and current observation `M1`:

```text
Active = C + MonotonicElapsed(M0, M1)
Remaining = max(ApprovedLimit - Active, 0)
Overtime = max(Active - ApprovedLimit, 0)
```

Before the approved limit, count-up displays `Active` and countdown displays `Remaining`.
At and after the limit both modes use the same boundary and positive `Overtime`; mode
changes presentation, not the work or transition rules.

Landing has its own monotonic segment and five-minute limit. Its elapsed duration is
included in active work and retained separately. For a Break with committed Break
duration `D`, positive approved limit `B`, and a new monotonic segment:

```text
BreakElapsed = min(D + MonotonicElapsed(M0, M1), B)
```

Break never changes the focus formulas.

## Representable states

Exactly one state exists at a time:

- `Ready`: configured and not started.
- `Focusing`: accruing ordinary focus time before the limit.
- `Paused`: stopped, with the exact focus phase to resume retained.
- `LimitReached`: stopped at either the focus or Landing boundary awaiting a choice.
- `Overtime`: accruing positive time beyond the approved focus limit.
- `Landing`: accruing the one optional five-minute Landing period.
- `Break`: accruing separate Break time toward its selected limit after Done or Park.
- `BreakCompleted`: stopped exactly at the Break limit, waiting for return confirmation.
- `Completed`: focus work ended as Done.
- `Parked`: focus work ended unfinished with a nonblank next physical action.
- `Abandoned`: focus work explicitly ended without promising a next action.
- `RecoveryRequired`: a running phase was interrupted and cannot resume implicitly.
- `DayClosed`: terminal state for the current day.

State-specific records carry state-specific data. Paused retains the focus phase,
LimitReached identifies the boundary, Break retains its approved limit, one selected
prompt, and Completed or Parked outcome, and RecoveryRequired retains the interrupted
phase. Unrelated booleans cannot describe contradictory combinations.

## Boundary, Landing, and extension rules

- The first observation exactly at the focus limit enters LimitReached. A delayed first
  observation beyond it enters Overtime and retains the full monotonic interval.
- Crossing a boundary emits its signal once. Repeated checks do not add time or emit it
  again.
- Continuing from exact focus LimitReached starts Overtime at the command instant. Time
  spent deciding is excluded.
- Landing can begin only after the focus limit and at most once. It counts upward from
  zero, stops after five active minutes, never extends automatically, and never begins a
  second Landing.
- A positive extension sets `NewApprovedLimit = CurrentActive + RequestedExtension` and
  resumes Focusing. Original planned duration remains unchanged.
- Done, Park, or Abandon first observes and commits the current active phase. Park
  requires a next physical action; Abandon is a distinct terminal outcome and creates no
  Context Capsule.
- Break begins only by explicit choice after Done or Park. Its positive configured limit
  and one prompt are fixed for that Break. It stops exactly at the limit, emits one
  idempotent signal, and waits in BreakCompleted.
- Ending Break early or confirming after its limit returns to the retained Completed or
  Parked outcome. It never starts focus or selects work automatically.

## Legal transitions

| Current state | Legal commands and outcomes |
| --- | --- |
| Ready | Start → Focusing; close day → DayClosed |
| Focusing | Refresh → Focusing, LimitReached, or Overtime; pause → Paused; Done → Completed; Park → Parked; Abandon → Abandoned; interrupt → RecoveryRequired |
| Paused | Resume → retained Focusing or Overtime phase; Done → Completed; Park → Parked; Abandon → Abandoned |
| LimitReached (focus) | Continue → Overtime; Landing → Landing; Extend → Focusing; Done → Completed; Park → Parked; Abandon → Abandoned |
| Overtime | Refresh → Overtime; pause → Paused; Landing → Landing; Extend → Focusing; Done → Completed; Park → Parked; Abandon → Abandoned; interrupt → RecoveryRequired |
| Landing | Refresh → Landing or LimitReached (Landing); Extend → Focusing; Done → Completed; Park → Parked; Abandon → Abandoned; interrupt → RecoveryRequired |
| LimitReached (Landing) | Extend → Focusing; Done → Completed; Park → Parked; Abandon → Abandoned |
| Completed | Begin Break → Break; close day → DayClosed |
| Parked | Begin Break → Break; close day → DayClosed |
| Break | Refresh → Break or BreakCompleted; End Break → retained outcome; interrupt → RecoveryRequired; close day → DayClosed |
| BreakCompleted | End Break → retained outcome; close day → DayClosed |
| RecoveryRequired (focus/Landing) | Resume excluding away → interrupted running phase; resume including bounded time → running phase or boundary; Done → Completed; Park → Parked; Abandon → Abandoned |
| RecoveryRequired (Break) | Resume excluding away → Break; resume including bounded time → Break or BreakCompleted |
| Abandoned | No state-changing command |
| DayClosed | No state-changing command |

Every other pair is illegal and fails without mutation. Done and Park never begin Break
automatically. Ending Break never starts another focus session. Day closure is rejected
while focus work or recovery remains unresolved.

## Durable recovery

A checkpoint persists identity, timing mode, original duration, approved limit, committed
focus/Landing/Break durations, durable state and state-specific context, and relevant UTC
timestamps. It never persists a monotonic anchor for another process.

- Ready, Paused, LimitReached, BreakCompleted, Completed, Parked, Abandoned, and DayClosed
  restore directly.
- Persisted or interrupted Focusing, Overtime, Landing, and Break restore as
  RecoveryRequired.
- Resume excluding away starts a fresh monotonic segment from committed duration. Any
  unobserved tail is lost rather than invented.
- Resume including away requires an explicit nonnegative duration no greater than the
  nonnegative observed UTC gap. If UTC moved backward, the allowable amount is zero.
- Included Landing time cannot exceed its remaining five-minute limit. Included Break
  time affects only Break, cannot exceed its remaining limit, and reaches BreakCompleted.
- Break limit, selected prompt, retained outcome, and parked next action survive restart.
- Identity, mode, original duration, and committed durations survive a new monotonic
  origin.

## Commit and concurrency rules

- Recovery-critical SQLite writes and task lifecycle changes commit in one explicit
  transaction before the App reports success. A successful Park commits its Context
  Capsule in that same transaction.
- Failed or cancelled persistence leaves the previously committed session authoritative.
- The App runtime serializes overlapping UI and power events with one narrow built-in
  asynchronous gate. Core remains a pure immutable transition function with no lock,
  timer loop, actor, channel, background worker, or dispatcher dependency.
- Suspension captures monotonic and UTC observations before waiting for the operation
  gate. Persistence delay cannot move the interruption boundary and invent focused time.
- Reapplying a boundary observation or restoration decision cannot double-count duration,
  repeat a cue, or mutate a terminal state.
