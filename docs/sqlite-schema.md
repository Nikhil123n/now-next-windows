# SQLite Schema

The current schema has four ordered versions. Prompt 3 introduced version 1 for the
current local working day. Prompt 4 preserves that migration and adds version 2 for one
durable current focus-session checkpoint. Prompt 6 adds version 3 for bounded Break
recovery, Context Capsules, and the local Break default. Prompt 7 adds version 4 for
protected day settings, schedule revisions, retained focus totals, repair acceptance/
undo, and durable closure; it does not add a general history view.

The authoritative version 1 migration is
[`0001_initial_today_plan.sql`](../src/NowNext.App/Persistence/Migrations/0001_initial_today_plan.sql).
The authoritative version 2 migration is
[`0002_current_focus_session_checkpoint.sql`](../src/NowNext.App/Persistence/Migrations/0002_current_focus_session_checkpoint.sql).
The authoritative version 3 migration is
[`0003_context_capsules_and_break_recovery.sql`](../src/NowNext.App/Persistence/Migrations/0003_context_capsules_and_break_recovery.sql).
The authoritative version 4 migration is
[`0004_schedule_recovery_shutdown.sql`](../src/NowNext.App/Persistence/Migrations/0004_schedule_recovery_shutdown.sql).
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

## Version 4 schedule repair, ledger, and closure

Version 4 adds a nonnegative `today_plans.schedule_revision`. Create, edit, delete,
reorder, lifecycle, day-setting, accepted-repair, and undo mutations increment it.
`day_settings` stores the explicitly selected shutdown and optional Daily Win for one
plan date. There is no implicit shutdown row or hidden default. The Daily Win and every
other retained task reference use `ON DELETE RESTRICT`.

`focus_session_records` retains one latest durable record per session. It stores plan and
task identity, timer mode, original/approved limits, committed active duration, Landing
and Break durations, durable state, and UTC start/end/checkpoint metadata. Saving the
current checkpoint updates this ledger in the same transaction. Migration 4 backfills
the existing singleton checkpoint by joining its retained task to its schedule entry.
Shutdown totals committed active duration, which includes Landing and excludes the
separate Break counter. These records support current totals and recovery; no history
screen is exposed.

`schedule_repairs` stores accepted proposal headers: trigger and observation, optional
extension, base revision, protected shutdown, total consumed buffer, revised finish, and
accept/undo timestamps. `schedule_repair_changes` records ordered before/after task
start, lifecycle, and position values. `schedule_repair_protections` retains every Fixed
interval used in the explanation. Acceptance requires the exact current revision and
shutdown and writes all changes/audit rows in one transaction. Undo selects only the
latest same-day non-undone repair and succeeds only when all affected values still equal
the recorded accepted result.

`day_closures` stores one confirmed closure per plan date with UTC closure time, planned
and actual totals, Daily Win result, and the selected most-important unfinished task and
next action. `day_closure_items` retains completed and deliberately deferred task totals.
After closure, production task/session/settings/repair mutations for that date fail.
Closure and any final DayClosed session checkpoint/ledger update commit together before
keep-awake release is requested.

## Migration safety

Initialization creates the migration registry and applies missing migrations in order in
one transaction. Unknown, renamed, or non-contiguous applied versions stop initialization.
A DDL conflict rolls back both the failed migration and the registry change. Tests cover
fresh version 0 to version 4, retained versions 1, 2, and 3 to version 4, checkpoint
backfill, repeat initialization, rollback, future versions, schema constraints,
checkpoint/task/capsule/repair foreign keys, and corrupt checkpoint or capsule data.
