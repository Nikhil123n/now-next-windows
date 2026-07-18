# SQLite Schema

Prompt 3 introduces schema version 1 for the current local working day only. The
authoritative migration is
[`0001_initial_today_plan.sql`](../src/NowNext.App/Persistence/Migrations/0001_initial_today_plan.sql).
Once committed to `main`, it must not be edited; later changes use a new ordered
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

## Migration safety

Initialization creates the migration registry and applies missing migrations in one
transaction. Unknown, renamed, or non-contiguous applied versions stop initialization.
A DDL conflict rolls back both the failed migration and the registry change. Tests cover
fresh version 0 to version 1, repeat initialization, rollback, future versions, and
foreign-key integrity.
