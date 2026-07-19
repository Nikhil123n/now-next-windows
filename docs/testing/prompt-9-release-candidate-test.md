# Prompt 9 release-candidate qualification

Run this script from a clean installation of the generated locally signed x64 MSIX
on the owner's Surface. Use disposable task text. Record facts and measured values; do
not invent performance thresholds or mark a hardware action passed without performing it.

## Environment record

Record the Surface model, Windows edition/build, display resolution/scaling, Windows App
Runtime version, package full name, power mode, AC/battery state, network state, and
Reduced Motion setting. Confirm Developer Mode is enabled, the package installs only
after the explicit elevated certificate-trust/install command, and it launches with
Wi-Fi disconnected.

## Clean-install P0 journey

1. Confirm Today opens with no prior tasks and remains usable offline.
2. Create a Flexible Count-up task and a Fixed Countdown task using every visible P0
   editor field. Confirm chronology, Fixed/Flexible labels, edit, reorder, and delete on
   a disposable third task. Configure a protected shutdown time.
3. Start Count-up. Confirm `00:00`, then pause with Space, wait, resume with Space, and
   confirm paused time was excluded. Also exercise touch pause/resume.
4. Reach the exact limit, confirm the one boundary, choose overtime with O, and confirm a
   positive `+` timer. Reveal controls intentionally and confirm Escape hides them.
5. Enter Landing with L. Confirm it counts up for five minutes and does not become an
   automatic extension. Park with P, supply a next physical action and optional short
   note, and confirm blank next action is rejected unless Abandon is explicitly chosen.
6. Start a five-minute Break with one built-in prompt. Confirm only one prompt is shown,
   return context appears near the end, E ends/confirms return, and no task starts until
   the explicit confirmation. Confirm the parked next action appears before restart.
7. Start Countdown. Confirm it begins at the planned duration, reaches `00:00`, and uses
   the same positive overtime boundary behavior. Extend by ten minutes with the revealed
   controls and confirm elapsed time remains authoritative.
8. Create the resulting one schedule-repair proposal. Confirm its trigger, buffer use,
   moved/deferred task, revised finish, protected Fixed commitment, and shutdown are
   explained. Reject once, then Accept and Undo. Confirm Fixed and shutdown never moved.
9. Complete work, review Shutdown totals and the highest-ranked unfinished next action,
   confirm Shutdown, and verify the durable resting state. Relaunch and confirm the day
   remains closed and mutation controls remain unavailable.

## Restart and forced termination matrix

Repeat with disposable data in each row. Let the periodic checkpoint commit, close or
force-terminate as specified, relaunch, and compare the exact committed value before and
after. Away time must never appear unless explicitly included.

| State | Normal close/restart | Forced termination | Expected recovery |
| --- | --- | --- | --- |
| Focusing | Record | Run recovery script | RecoveryRequired, Focus, committed time only |
| Paused | Record | Run recovery script | Paused, unchanged |
| Limit Reached | Record | Run recovery script | Limit Reached, boundary remains idempotent |
| Overtime | Record | Run recovery script | RecoveryRequired, Overtime, positive committed overtime |
| Landing | Record | Run recovery script | RecoveryRequired, Landing, committed Landing only |
| Break | Record | Run recovery script | RecoveryRequired, Break, committed Break only |

For a real Windows resume, repeat Focusing, Landing, and Break using the power menu and
the Surface lid/Type Cover behavior. Confirm explicit sleep is not blocked, keep-awake is
released, resume never starts automatically, and a late return shows one calm Recovery
panel with the next Fixed commitment and realistic nonnegative available time. Exercise
Resume, Rebuild, End as Done, End as Park, Abandon, and Close early on separate disposable
sessions.

## Accessibility and input

1. With Windows Animation effects on, confirm the colon blinks once per second without
   moving the numerals. Turn Animation effects off and confirm the colon remains static
   while timer progression and controls are unchanged.
2. Complete Focus using Space, F, P, L, O, E, and Escape; complete Break using E. Confirm
   visible keyboard focus, logical order, and understandable accessible names.
3. Repeat the main path with touch at the Surface landscape resolution. Check 200% text
   scaling and a Windows High Contrast theme for readable content and reachable actions.

## Data safety and uninstall

1. Back up a plan containing both timer modes, a Context Capsule, day settings, and an
   active committed checkpoint. Change the plan, restore, and confirm every item and the
   Recovery state return exactly. Reject a corrupt `.db` without changing live data.
2. Inspect the default JSON-lines diagnostic log for the exact disposable title, focus
   label, next action, note, and capsule phrase. None may appear.
3. Run the measurement command for its full 60-minute default. Record cold start, idle
   normalized CPU, idle working/private memory, maximum long-run memory, processor time,
   sample count, responsiveness, AC/battery state, and any process exit or visible drift.
4. Export a validated database outside LocalState. Uninstall the package, confirm the
   Start entry and package LocalState are removed, confirm the external export remains,
   reinstall, and restore from that export.

## Result record

Automated results, packaged build/install evidence, device measurements, and each manual
section's pass/fail/not-run status belong in the Prompt 9 implementation plan. Any tested
crash that loses a committed checkpoint, invented away time, moved Fixed/shutdown value,
failed validated restore, inaccessible P0 command, or packaged launch failure blocks the
release candidate. A not-run hardware scenario remains an explicit limitation, not a pass.
