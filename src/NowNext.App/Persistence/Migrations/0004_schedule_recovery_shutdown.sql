ALTER TABLE today_plans
ADD COLUMN schedule_revision INTEGER NOT NULL DEFAULT 0
    CHECK (schedule_revision >= 0);

CREATE TABLE day_settings (
    plan_date TEXT NOT NULL PRIMARY KEY,
    shutdown_time TEXT NOT NULL,
    daily_win_task_id TEXT NULL,
    FOREIGN KEY (plan_date) REFERENCES today_plans(plan_date) ON DELETE CASCADE,
    FOREIGN KEY (daily_win_task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT
);

CREATE TABLE focus_session_records (
    session_id TEXT NOT NULL PRIMARY KEY
        CHECK (length(session_id) = 36),
    plan_date TEXT NOT NULL,
    task_id TEXT NOT NULL
        CHECK (length(task_id) = 36),
    timing_mode TEXT NOT NULL
        CHECK (timing_mode IN ('CountUp', 'Countdown')),
    original_planned_duration_ticks INTEGER NOT NULL
        CHECK (original_planned_duration_ticks > 0),
    approved_limit_ticks INTEGER NOT NULL
        CHECK (approved_limit_ticks >= original_planned_duration_ticks),
    committed_active_ticks INTEGER NOT NULL
        CHECK (committed_active_ticks >= 0),
    landing_ticks INTEGER NOT NULL
        CHECK (landing_ticks >= 0 AND landing_ticks <= committed_active_ticks),
    break_ticks INTEGER NOT NULL
        CHECK (break_ticks >= 0),
    session_state TEXT NOT NULL
        CHECK (session_state IN (
            'Ready', 'Paused', 'LimitReached', 'Completed', 'Parked',
            'BreakCompleted', 'Abandoned', 'RecoveryRequired', 'DayClosed')),
    started_at_utc TEXT NULL,
    ended_at_utc TEXT NULL,
    checkpointed_at_utc TEXT NOT NULL,
    FOREIGN KEY (plan_date) REFERENCES today_plans(plan_date) ON DELETE RESTRICT,
    FOREIGN KEY (task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT
);

INSERT INTO focus_session_records(
    session_id,
    plan_date,
    task_id,
    timing_mode,
    original_planned_duration_ticks,
    approved_limit_ticks,
    committed_active_ticks,
    landing_ticks,
    break_ticks,
    session_state,
    started_at_utc,
    ended_at_utc,
    checkpointed_at_utc)
SELECT c.session_id,
       e.plan_date,
       c.task_id,
       c.timing_mode,
       c.original_planned_duration_ticks,
       c.approved_limit_ticks,
       c.committed_active_ticks,
       c.landing_ticks,
       c.break_ticks,
       c.session_state,
       c.started_at_utc,
       COALESCE(c.completed_at_utc, c.parked_at_utc, c.abandoned_at_utc, c.day_closed_at_utc),
       c.checkpointed_at_utc
FROM current_session_checkpoint AS c
INNER JOIN schedule_entries AS e ON e.task_id = c.task_id;

CREATE INDEX index_focus_session_records_plan_task
ON focus_session_records(plan_date, task_id);

CREATE TABLE schedule_repairs (
    repair_id TEXT NOT NULL PRIMARY KEY
        CHECK (length(repair_id) = 36),
    plan_date TEXT NOT NULL,
    trigger_kind TEXT NOT NULL
        CHECK (trigger_kind IN ('SessionExtended', 'CurrentTime', 'RecoveryRebuild')),
    trigger_observed_at_utc TEXT NOT NULL,
    extension_ticks INTEGER NULL
        CHECK (extension_ticks IS NULL OR extension_ticks > 0),
    base_revision INTEGER NOT NULL
        CHECK (base_revision >= 0),
    shutdown_time TEXT NOT NULL,
    buffer_consumed_ticks INTEGER NOT NULL
        CHECK (buffer_consumed_ticks >= 0),
    revised_finish_ticks INTEGER NOT NULL
        CHECK (revised_finish_ticks >= 0 AND revised_finish_ticks < 864000000000),
    accepted_at_utc TEXT NOT NULL,
    undone_at_utc TEXT NULL,
    FOREIGN KEY (plan_date) REFERENCES today_plans(plan_date) ON DELETE RESTRICT
);

CREATE INDEX index_schedule_repairs_plan_accepted
ON schedule_repairs(plan_date, accepted_at_utc DESC);

CREATE TABLE schedule_repair_changes (
    repair_id TEXT NOT NULL,
    task_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL
        CHECK (ordinal >= 0),
    previous_start TEXT NOT NULL,
    revised_start TEXT NOT NULL,
    previous_position INTEGER NOT NULL,
    revised_position INTEGER NOT NULL,
    previous_state TEXT NOT NULL
        CHECK (previous_state IN ('Planned', 'Active', 'Completed', 'Parked', 'Deferred')),
    revised_state TEXT NOT NULL
        CHECK (revised_state IN ('Planned', 'Active', 'Completed', 'Parked', 'Deferred')),
    PRIMARY KEY (repair_id, task_id),
    UNIQUE (repair_id, ordinal),
    FOREIGN KEY (repair_id) REFERENCES schedule_repairs(repair_id) ON DELETE CASCADE,
    FOREIGN KEY (task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT
);

CREATE TABLE schedule_repair_protections (
    repair_id TEXT NOT NULL,
    task_id TEXT NOT NULL,
    planned_start TEXT NOT NULL,
    planned_duration_ticks INTEGER NOT NULL
        CHECK (planned_duration_ticks > 0),
    PRIMARY KEY (repair_id, task_id),
    FOREIGN KEY (repair_id) REFERENCES schedule_repairs(repair_id) ON DELETE CASCADE,
    FOREIGN KEY (task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT
);

CREATE TABLE day_closures (
    plan_date TEXT NOT NULL PRIMARY KEY,
    closed_at_utc TEXT NOT NULL,
    total_planned_ticks INTEGER NOT NULL
        CHECK (total_planned_ticks >= 0),
    total_actual_ticks INTEGER NOT NULL
        CHECK (total_actual_ticks >= 0),
    daily_win_task_id TEXT NULL,
    daily_win_status TEXT NOT NULL
        CHECK (daily_win_status IN ('NotSelected', 'Completed', 'NotCompleted')),
    next_unfinished_task_id TEXT NULL,
    next_physical_action TEXT NULL
        CHECK (next_physical_action IS NULL OR length(trim(next_physical_action)) > 0),
    FOREIGN KEY (plan_date) REFERENCES today_plans(plan_date) ON DELETE RESTRICT,
    FOREIGN KEY (daily_win_task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT,
    FOREIGN KEY (next_unfinished_task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT,
    CHECK (
        (next_unfinished_task_id IS NULL AND next_physical_action IS NULL)
        OR (next_unfinished_task_id IS NOT NULL AND next_physical_action IS NOT NULL))
);

CREATE TABLE day_closure_items (
    plan_date TEXT NOT NULL,
    task_id TEXT NOT NULL,
    outcome TEXT NOT NULL
        CHECK (outcome IN ('Completed', 'Deferred')),
    planned_duration_ticks INTEGER NOT NULL
        CHECK (planned_duration_ticks > 0),
    actual_duration_ticks INTEGER NOT NULL
        CHECK (actual_duration_ticks >= 0),
    PRIMARY KEY (plan_date, task_id),
    FOREIGN KEY (plan_date) REFERENCES day_closures(plan_date) ON DELETE CASCADE,
    FOREIGN KEY (task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT
);
