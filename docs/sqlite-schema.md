# SQLite Schema

The current schema has three ordered versions. Prompt 3 introduced version 1 for the
current local working day. Prompt 4 preserves that migration and adds version 2 for one
durable current focus-session checkpoint. Prompt 6 adds version 3 for bounded Break
recovery, Context Capsules, and the local Break default; it does not add general session
history.

The authoritative version 1 migration is
[`0001_initial_today_plan.sql`](../src/NowNext.App/Persistence/Migrations/0001_initial_today_plan.sql).
The authoritative version 2 migration is
[`0002_current_focus_session_checkpoint.sql`](../src/NowNext.App/Persistence/Migrations/0002_current_focus_session_checkpoint.sql).
The authoritative version 3 migration is
[`0003_context_capsules_and_break_recovery.sql`](../src/NowNext.App/Persistence/Migrations/0003_context_capsules_and_break_recovery.sql).
Once committed to `main`, a migration must not be edited; later changes use a new ordered
migration.

## Storage location and connection rules

The packaged application stores `now-next.db` in
`ApplicationData.Current.LocalFolder`, the current package user's LocalState directory.
Tests always supply an isolated explicit path. Every connection enables foreign keys.
Every mutation and migration runs in an explicit transaction, and all task data uses
parameters.

## Version 1 tables

| Table | Purpose and keys |
| --- | --- |
| `schema_migrations` | Applied `version`, stable `name`, and invariant UTC timestamp. Versions must be known and contiguous from 1. |
| `today_plans` | One row per local `plan_date`, stored as `yyyy-MM-dd`. Prompt 3 operations address only the injected clock's current date. |
| `tasks` | Stable task ID, all Today domain fields, enum names, duration ticks, and nullable `deleted_at_utc`. Required text and enum values have checks. |
| `schedule_entries` | Ordered task membership for a plan. `(plan_date, task_id)` is primary, position is unique per plan, and each task has at most one plan entry. |

Task IDs use canonical `D` GUID text. Planned starts use invariant `TimeOnly` text,
durations use ticks, and timestamps use round-trip invariant UTC text. Enum names are
stored rather than ordinals so invalid values fail clearly.

`schedule_entries.task_id` references `tasks.task_id` with `ON DELETE RESTRICT`.
Deleting today's task removes its schedule entry, compacts remaining positions, and
stamps the task row; it never physically deletes the row. This preserves a stable target
for later session-history work without adding a history table in this milestone.

## Version 2 current-session checkpoint

Version 2 adds `current_session_checkpoint`, a single-row table whose fixed slot is `1`,
rather than an append-only event or history model. The record preserves the minimum
durable state required to restore a session safely:

- session and task identity;
- timing mode, original planned duration, and current approved limit;
- committed focus, Landing, and Break durations;
- the durable session state and its state-specific recovery context;
- relevant invariant UTC timestamps; and
- the parked next physical action only when that state requires it.

The session's task reference uses `ON DELETE RESTRICT`. Production task deletion remains
soft deletion and is rejected while the task owns an unresolved current session. Duration
checks reject negative committed values, nonpositive planned/approved limits, approved
limits shorter than the original plan, and Landing duration beyond either committed
active time or five minutes. Text checks restrict persisted state and timing values to
the Core contract and require state-specific values only in the states that own them.

No process-local monotonic timestamp is durable. A running checkpoint loaded after a
process loss or suspension becomes RecoveryRequired; the new process starts a fresh
monotonic segment from committed duration. Session checkpoint and corresponding task
lifecycle changes commit in the same explicit transaction.

## Version 3 Context Capsule and Break recovery

Version 3 rebuilds the single checkpoint table without discarding version 2 data. The
new shape adds `Abandoned` and `BreakCompleted` durable states plus the bounded Break
limit and one selected prompt required to restore a running or completed Break. Prompt
kinds are restricted to distant gaze, water, jaw relaxation, shoulder release, stand,
walk, or user-selected movement. A running Break is checkpointed as RecoveryRequired;
Break elapsed is capped at its positive limit and BreakCompleted waits without accruing.

`context_capsules` appends one capsule per parking session. Its session ID is the primary
key; its retained task reference uses `ON DELETE RESTRICT`. Each record contains only the
required next physical action, optional nonblank short note, invariant UTC save timestamp,
task ID, and session ID. Loading a task selects its latest capsule by timestamp. Parking
updates the task lifecycle, current checkpoint, and capsule inside one transaction before
the runtime publishes success. Soft deletion removes only plan membership, so a saved
capsule remains available.

`break_settings` is an optional singleton row containing a positive default duration and
optional nonblank user-selected movement. Absence of the row means the product default of
five minutes. It is local presentation configuration, not an independent timer or health
tracking model.

## Migration safety

Initialization creates the migration registry and applies missing migrations in order in
one transaction. Unknown, renamed, or non-contiguous applied versions stop initialization.
A DDL conflict rolls back both the failed migration and the registry change. Tests cover
fresh version 0 to version 3, retained versions 1 and 2 to version 3, repeat
initialization, rollback, future versions, checkpoint/task/capsule foreign keys, and
corrupt checkpoint or capsule data.
