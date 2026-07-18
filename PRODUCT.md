# NOW/NEXT Product

NOW/NEXT is a dedicated Windows 11 application for one person using one Surface. It
helps the user make a realistic plan for today, see only the work that matters now,
measure focused time honestly, make deliberate transitions, preserve the next action,
repair the remainder of the day, recover safely, and close the workday.

The complete approved capability list is [FEATURES_FORWARD.md](FEATURES_FORWARD.md).
The complete deferred and removed register is
[FEATURES_DEFERRED_OR_REMOVED.md](FEATURES_DEFERRED_OR_REMOVED.md). Both are
authoritative and must be read before changing product behavior.

## Intended user and environment

- One user on one dedicated Windows 11 Surface.
- Touch and keyboard are the prototype input methods.
- The application remains fully functional offline.
- It is a day-execution tool, not a project-management backlog.

## Core journey

Plan today → start a task → use count-up or countdown → pause or resume → reach the
limit → choose overtime, Landing, Done, Park, or Extend → take a break → return with
the saved next physical action → repair the day when needed → recover after restart or
sleep → close the workday.

## Product invariants

1. Count-up and countdown are equal, first-class timing modes. Count-up is only the
   recommended default.
2. The focus screen normally shows only the short focus label and timer.
3. The timer colon blinks at a calm one-second rhythm without layout movement. When
   Windows Reduced Motion is enabled, the colon may remain static.
4. Elapsed focus time represents committed active work. Sleep, suspension, crashes,
   and unobserved absence are never silently counted.
5. Fixed commitments and planned shutdown are protected. Moving either requires an
   explicit, visible user decision.
6. Schedule repair is deterministic. The user sees every effect and approves it before
   anything changes.
7. An unfinished task is parked with a next physical action unless explicitly abandoned.
8. A transition never starts the next focus session automatically.
9. Recovery-critical writes commit before the interface reports success.
10. Product operation requires no account, cloud, synchronization, backend, calendar,
    AI, telemetry service, or external API.

## Prototype success

The required journey must survive 10–14 real working days without losing session
state, inventing focus time, silently moving Fixed commitments, or depending on cloud
availability. Functional correctness and accessibility come before decorative polish.
