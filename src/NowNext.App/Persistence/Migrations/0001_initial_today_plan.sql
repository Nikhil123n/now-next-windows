CREATE TABLE today_plans (
    plan_date TEXT NOT NULL PRIMARY KEY
        CHECK (length(plan_date) = 10)
);

CREATE TABLE tasks (
    task_id TEXT NOT NULL PRIMARY KEY
        CHECK (length(task_id) = 36),
    full_title TEXT NOT NULL
        CHECK (length(trim(full_title)) > 0),
    short_focus_label TEXT NOT NULL
        CHECK (length(trim(short_focus_label)) > 0),
    definition_of_done TEXT NOT NULL
        CHECK (length(trim(definition_of_done)) > 0),
    first_physical_action TEXT NOT NULL
        CHECK (length(trim(first_physical_action)) > 0),
    next_physical_action TEXT NULL
        CHECK (next_physical_action IS NULL OR length(trim(next_physical_action)) > 0),
    planned_start TEXT NOT NULL,
    planned_duration_ticks INTEGER NOT NULL
        CHECK (planned_duration_ticks > 0),
    timing_mode TEXT NOT NULL
        CHECK (timing_mode IN ('CountUp', 'Countdown')),
    schedule_type TEXT NOT NULL
        CHECK (schedule_type IN ('Fixed', 'Flexible')),
    importance TEXT NOT NULL
        CHECK (importance IN ('Normal', 'Important')),
    task_state TEXT NOT NULL
        CHECK (task_state IN ('Planned', 'Active', 'Completed', 'Parked', 'Deferred')),
    deleted_at_utc TEXT NULL
);

CREATE TABLE schedule_entries (
    plan_date TEXT NOT NULL,
    task_id TEXT NOT NULL,
    position INTEGER NOT NULL,
    PRIMARY KEY (plan_date, task_id),
    UNIQUE (plan_date, position),
    UNIQUE (task_id),
    FOREIGN KEY (plan_date) REFERENCES today_plans(plan_date) ON DELETE CASCADE,
    FOREIGN KEY (task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT
);
