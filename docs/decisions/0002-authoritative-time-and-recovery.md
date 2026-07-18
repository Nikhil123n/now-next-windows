# 0002 — Authoritative time and recovery

- Status: Accepted
- Date: 2026-07-18

## Context

UI timers can pause during sleep, suspension, crashes, and process shutdown. Treating
wall-clock difference as focused work would invent time and violate the product promise.

## Decision

Core owns the session state machine and uses `TimeProvider`. Persist committed active
duration and recovery checkpoints. On an interruption or unobserved interval, require an
explicit choice to resume committed time, include away time, end the session, or rebuild
the day. Count-up and countdown share transitions and both show positive overtime.

## Consequences

The UI renders authoritative state rather than owning elapsed time. Recovery paths are
first-class test cases. Sleep and suspension are never silently credited. A monotonic
source is used while running; persisted UTC timestamps support recovery decisions.

## Supersedes

None.
