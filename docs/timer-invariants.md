# Timer and Session Invariants

This document defines the Prompt 4 authoritative timing and recovery contract. It is a
behavioral companion to [ADR 0002](decisions/0002-authoritative-time-and-recovery.md) and
does not authorize task/timer UI or later product features.

## Sources of time

- While a process is running, active elapsed duration comes only from the monotonic
  `TimeProvider.GetTimestamp()`/`GetElapsedTime()` pair.
- UTC wall time records persistence and schedule presentation. It is never the source of
  live active elapsed duration.
- A UI refresh asks for a projection of current state. It does not add a tick. Delayed,
  skipped, or repeated rendering cannot change authoritative elapsed time.
- Paused, limit-decision, completed, parked, recovery-required, and day-closed states do
  not accrue active time.
- Break elapsed is separate and never contributes to focused time.

For a running focus or Landing segment with committed total active duration `C`,
monotonic segment anchor `M0`, and current monotonic observation `M1`:

```text
Active = C + MonotonicElapsed(M0, M1)
Remaining = max(ApprovedLimit - Active, 0)
Overtime = max(Active - ApprovedLimit, 0)
```

Before the approved limit, count-up displays `Active` and countdown displays `Remaining`.
At and after the limit both modes use the same boundary and positive `Overtime`; the
selected mode changes presentation, not the recorded work or transition rules.

Landing has its own monotonic segment and five-minute limit. Its elapsed duration is
included in actual active work but is also retained separately for audit and projection.
Break uses a separate elapsed duration and never changes the focus formulas.

## Representable states

Exactly one of these states exists at a time:

- `Ready`: configured and not started.
- `Focusing`: actively accruing ordinary focus time before the limit.
- `Paused`: stopped, with the exact phase to resume retained.
- `LimitReached`: stopped at either the focus or Landing boundary awaiting a choice.
- `Overtime`: actively accruing positive time beyond the approved focus limit.
- `Landing`: actively accruing the one optional five-minute Landing period.
- `Break`: accruing break time separately after a completed or parked outcome.
- `Completed`: focus work ended as done.
- `Parked`: focus work ended unfinished with a nonblank next physical action.
- `RecoveryRequired`: a running phase was interrupted and cannot resume implicitly.
- `DayClosed`: terminal state for the current day.

State-specific records carry state-specific data. For example, Paused retains whether it
resumes Focusing or Overtime, LimitReached identifies the focus or Landing boundary, Break
retains its Completed or Parked outcome, and RecoveryRequired retains the interrupted
phase. Unrelated booleans cannot describe contradictory combinations.

## Boundary and extension rules

- A refresh before the approved focus limit remains Focusing.
- The first observation exactly at the limit enters LimitReached for the focus boundary.
- The first delayed observation beyond the limit enters Overtime directly and retains
  the full monotonic active interval.
- Crossing a boundary emits its signal once. Refreshing or checking again in
  LimitReached or Overtime does not add time or emit the boundary again.
- Choosing continued overtime from an exact focus LimitReached state starts a new active
  segment at the command instant. Time spent deciding is excluded.
- Landing can begin only after the focus limit and at most once. It counts upward from
  zero and stops exactly after five active minutes in LimitReached for Landing.
- Landing never extends automatically and cannot start another Landing.
- An extension duration must be positive. At command time, its result is:

```text
NewApprovedLimit = CurrentActiveElapsed + RequestedExtension
```

This grants the full requested time even if the session is already in overtime. The
original planned duration remains unchanged. Extending from a boundary, Overtime, or
Landing leaves the decision/Landing phase and resumes ordinary Focusing under the new
approved limit.

- Completing or parking an active phase first observes and commits that phase's monotonic
  elapsed duration at the command instant.

## Legal transitions

| Current state | Legal commands and outcomes |
| --- | --- |
| Ready | Start → Focusing; close day → DayClosed |
| Focusing | Refresh/checkpoint → Focusing, LimitReached, or Overtime; pause → Paused; complete → Completed; park → Parked; interrupt → RecoveryRequired |
| Paused | Resume → retained Focusing or Overtime phase; complete → Completed; park → Parked |
| LimitReached (focus) | Continue → Overtime; begin Landing → Landing; extend → Focusing; complete → Completed; park → Parked |
| Overtime | Refresh/checkpoint → Overtime; pause → Paused; begin Landing → Landing; extend → Focusing; complete → Completed; park → Parked; interrupt → RecoveryRequired |
| Landing | Refresh/checkpoint → Landing or LimitReached (Landing); extend → Focusing; complete → Completed; park → Parked; interrupt → RecoveryRequired |
| LimitReached (Landing) | Extend → Focusing; complete → Completed; park → Parked |
| Completed | Begin Break → Break; close day → DayClosed |
| Parked | Begin Break → Break; close day → DayClosed |
| Break | Refresh/checkpoint → Break; end Break → retained Completed or Parked outcome; interrupt → RecoveryRequired; close day → DayClosed |
| RecoveryRequired | Resume excluding away → interrupted running phase; resume including an explicit bounded duration → interrupted running phase; complete → Completed; park → Parked |
| DayClosed | No state-changing command |

Every other state/command pair is illegal and fails without mutation. Parking requires a
trimmed nonblank next physical action. Completion and parking never begin Break
automatically; ending Break never starts another focus session. Day closure is rejected
while focus work or recovery remains unresolved.

## Durable recovery

A checkpoint persists identity, timing mode, original planned duration, current approved
limit, committed focus/Landing/Break durations, the durable state and state-specific
context, and relevant UTC timestamps. It does not persist a monotonic anchor for reuse by
another process.

- Ready, Paused, LimitReached, Completed, Parked, and DayClosed restore directly.
- Persisted or interrupted Focusing, Overtime, Landing, and Break restore as
  RecoveryRequired after process loss or suspension.
- Resume excluding away starts a fresh monotonic segment from the last committed
  duration. Any unobserved tail is lost rather than invented.
- Resume including away requires an explicit nonnegative duration. It cannot exceed the
  nonnegative observed UTC gap between checkpoint and recovery.
- If UTC moved backward, the allowable inferred gap is zero. UTC clock changes never
  subtract committed work or create positive focus.
- Included Landing time cannot exceed the remainder of its five-minute limit. Included
  Break time affects only Break elapsed.
- Identity, timing mode, original planned duration, and committed durations survive
  restart. A new process never compares its monotonic timestamp origin with the old one.

## Commit and concurrency rules

- Recovery-critical SQLite writes and their task lifecycle update commit in one explicit
  transaction before the App reports success.
- Failed or cancelled persistence leaves the previously committed session authoritative.
- Overlapping UI and power events are serialized by the narrow App runtime. Core remains
  a pure immutable transition function with no lock, thread, timer loop, actor, channel,
  background worker, or dispatcher dependency.
- A Windows suspension event captures its monotonic and UTC observation before waiting
  for the App operation gate. Persistence delay or resume cannot move that interruption
  boundary forward and silently turn suspended time into focus.
- Reapplying a boundary observation or restoration decision cannot double-count duration,
  repeat a cue, or mutate a terminal state.
