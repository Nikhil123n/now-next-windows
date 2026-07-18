CREATE TABLE current_session_checkpoint (
    slot INTEGER NOT NULL PRIMARY KEY
        CHECK (slot = 1),
    session_id TEXT NOT NULL UNIQUE
        CHECK (length(session_id) = 36),
    task_id TEXT NOT NULL
        CHECK (length(task_id) = 36),
    timing_mode TEXT NOT NULL
        CHECK (timing_mode IN ('CountUp', 'Countdown')),
    original_planned_duration_ticks INTEGER NOT NULL
        CHECK (original_planned_duration_ticks > 0),
    approved_limit_ticks INTEGER NOT NULL
        CHECK (approved_limit_ticks > 0),
    session_state TEXT NOT NULL
        CHECK (session_state IN (
            'Ready',
            'Paused',
            'LimitReached',
            'Completed',
            'Parked',
            'RecoveryRequired',
            'DayClosed')),
    committed_active_ticks INTEGER NOT NULL
        CHECK (committed_active_ticks >= 0),
    landing_ticks INTEGER NOT NULL
        CHECK (landing_ticks >= 0),
    break_ticks INTEGER NOT NULL
        CHECK (break_ticks >= 0),
    checkpointed_at_utc TEXT NOT NULL
        CHECK (length(trim(checkpointed_at_utc)) > 0),
    resume_phase TEXT NULL
        CHECK (resume_phase IS NULL OR resume_phase IN ('Focusing', 'Overtime')),
    boundary_kind TEXT NULL
        CHECK (boundary_kind IS NULL OR boundary_kind IN ('FocusLimit', 'LandingLimit')),
    recovery_phase TEXT NULL
        CHECK (recovery_phase IS NULL OR recovery_phase IN (
            'Focusing',
            'Overtime',
            'Landing',
            'Break')),
    prior_outcome TEXT NULL
        CHECK (prior_outcome IS NULL OR prior_outcome IN ('Completed', 'Parked')),
    started_at_utc TEXT NULL,
    completed_at_utc TEXT NULL,
    parked_at_utc TEXT NULL,
    day_closed_at_utc TEXT NULL,
    parked_next_physical_action TEXT NULL
        CHECK (
            parked_next_physical_action IS NULL
            OR length(trim(parked_next_physical_action)) > 0),
    FOREIGN KEY (task_id) REFERENCES tasks(task_id) ON DELETE RESTRICT,
    CHECK (approved_limit_ticks >= original_planned_duration_ticks),
    CHECK (landing_ticks <= committed_active_ticks),
    CHECK (landing_ticks <= 3000000000),
    CHECK (
        (session_state = 'Ready'
            AND resume_phase IS NULL
            AND boundary_kind IS NULL
            AND recovery_phase IS NULL
            AND prior_outcome IS NULL
            AND completed_at_utc IS NULL
            AND parked_at_utc IS NULL
            AND day_closed_at_utc IS NULL
            AND parked_next_physical_action IS NULL)
        OR (session_state = 'Paused'
            AND resume_phase IS NOT NULL
            AND boundary_kind IS NULL
            AND recovery_phase IS NULL
            AND prior_outcome IS NULL
            AND completed_at_utc IS NULL
            AND parked_at_utc IS NULL
            AND day_closed_at_utc IS NULL
            AND parked_next_physical_action IS NULL)
        OR (session_state = 'LimitReached'
            AND resume_phase IS NULL
            AND boundary_kind IS NOT NULL
            AND recovery_phase IS NULL
            AND prior_outcome IS NULL
            AND completed_at_utc IS NULL
            AND parked_at_utc IS NULL
            AND day_closed_at_utc IS NULL
            AND parked_next_physical_action IS NULL)
        OR (session_state = 'Completed'
            AND resume_phase IS NULL
            AND boundary_kind IS NULL
            AND recovery_phase IS NULL
            AND prior_outcome IS NULL
            AND completed_at_utc IS NOT NULL
            AND parked_at_utc IS NULL
            AND day_closed_at_utc IS NULL
            AND parked_next_physical_action IS NULL)
        OR (session_state = 'Parked'
            AND resume_phase IS NULL
            AND boundary_kind IS NULL
            AND recovery_phase IS NULL
            AND prior_outcome IS NULL
            AND completed_at_utc IS NULL
            AND parked_at_utc IS NOT NULL
            AND day_closed_at_utc IS NULL
            AND parked_next_physical_action IS NOT NULL)
        OR (session_state = 'RecoveryRequired'
            AND resume_phase IS NULL
            AND boundary_kind IS NULL
            AND recovery_phase IS NOT NULL
            AND day_closed_at_utc IS NULL
            AND (
                (recovery_phase <> 'Break'
                    AND prior_outcome IS NULL
                    AND completed_at_utc IS NULL
                    AND parked_at_utc IS NULL
                    AND parked_next_physical_action IS NULL)
                OR (recovery_phase = 'Break'
                    AND (
                        (prior_outcome = 'Completed'
                            AND completed_at_utc IS NOT NULL
                            AND parked_at_utc IS NULL
                            AND parked_next_physical_action IS NULL)
                        OR (prior_outcome = 'Parked'
                            AND completed_at_utc IS NULL
                            AND parked_at_utc IS NOT NULL
                            AND parked_next_physical_action IS NOT NULL)))))
        OR (session_state = 'DayClosed'
            AND resume_phase IS NULL
            AND boundary_kind IS NULL
            AND recovery_phase IS NULL
            AND day_closed_at_utc IS NOT NULL
            AND (
                (prior_outcome IS NULL
                    AND completed_at_utc IS NULL
                    AND parked_at_utc IS NULL
                    AND parked_next_physical_action IS NULL)
                OR (prior_outcome = 'Completed'
                    AND completed_at_utc IS NOT NULL
                    AND parked_at_utc IS NULL
                    AND parked_next_physical_action IS NULL)
                OR (prior_outcome = 'Parked'
                    AND completed_at_utc IS NULL
                    AND parked_at_utc IS NOT NULL
                    AND parked_next_physical_action IS NOT NULL))))
);
