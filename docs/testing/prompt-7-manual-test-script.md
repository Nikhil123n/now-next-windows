# Prompt 7 Manual Test Script

Use a packaged Windows 11 build and a disposable prototype database. Record Windows
build, architecture, app/runtime versions, input method, display scale, and any skipped
hardware-only checks. Do not enter sensitive task content.

## Setup

1. Reset only the prototype package database with
   `scripts\Database-Dev.ps1 -Reset -ConfirmReset`, then launch the packaged App.
2. Create an Important Flexible task, a later Normal Flexible task, and two Fixed tasks
   with visible gaps. Use both count-up and countdown across the tasks.
3. Select today's shutdown explicitly and choose the Important task as Daily Win.

## Extension repair, explanation, and undo

1. Start the first Flexible task, reach its limit, choose Extend, and confirm exactly one
   repair callout appears. Verify nothing moved before opening or accepting it.
2. Review the proposal. Confirm it identifies the extension trigger, total buffer and
   each consumed gap, every move/deferral, revised finish, both protected Fixed tasks,
   and unchanged shutdown.
3. Close the proposal. Confirm no schedule change. Reopen and Accept; confirm only the
   proposed Flexible fields/order change.
4. Choose Undo repair. Confirm the prior starts, states, and order return. Edit an
   affected task after another acceptance and verify stale undo refuses rather than
   overwriting the edit.
5. Create zero-buffer and impossible cases. Confirm the recommendation uses one eligible
   Normal task before an Important task and that an impossible proposal cannot be
   accepted.

## Protected edits and current-time repair

1. Edit a Fixed task's label only; confirm no unlock is needed. Change its time,
   duration, or type; confirm save requires the visible Fixed-unlock checkbox.
2. Change an existing shutdown. Confirm a visible dialog shows old and new values and
   defaults to keeping the protected time.
3. Return late enough that the remaining plan no longer fits. Confirm Today shows one
   calm review callout, not a red overdue list or a menu of alternatives.

## Recovery Mode

1. Run focus for several minutes and ensure a durable checkpoint is written. Terminate
   and restart. Confirm Recovery shows the interrupted task, next Fixed commitment, and
   nonnegative realistic time before the earlier of that commitment or shutdown.
2. Resume excluding away time and confirm only committed focus is retained. Repeat and
   explicitly include a bounded amount; confirm the inclusion is deliberate and visible.
3. Exercise keyboard, touch, and pointer paths in order: Resume, include-and-resume,
   Rebuild remaining day, End interrupted session, and Close work early. Verify visible
   focus, logical tab order, and 44-pixel-or-larger action targets.
4. For End, choose Done, Park with a required next action, and explicit Abandon on
   separate runs. Confirm Rebuild opens a proposal but never resumes or ends focus.
5. Suspend or leave the foreground for exactly 15 minutes after a committed checkpoint.
   Confirm Recovery excludes the unobserved tail. Repeat with a shorter UI delay and
   confirm normal monotonic projection continues.

## Shutdown and resting state

1. Complete one task, deliberately defer one, and accumulate multiple sessions including
   Landing and Break. Open Shutdown and verify completed/deferred lists, planned versus
   actual active time, Daily Win status, and the highest-ranked unfinished task's latest
   Context Capsule action.
2. Confirm Break time is excluded and Landing active time is included. Close the dialog;
   verify the day remains open.
3. Confirm Shutdown. Verify the plain resting state appears only after persistence and
   that task, session, repair, settings, and reorder actions are disabled.
4. Restart. Confirm the stored summary/resting state returns and no task or session
   starts automatically. Verify a failed keep-awake release does not reopen the day if a
   Windows-integration test implementation is available.

## Accessibility and regression

1. Repeat repair, Recovery, and Shutdown with keyboard only and touch only. Check logical
   focus order, visible focus, accessible names, 200% text scaling, and High Contrast.
2. Confirm normal Focus still contains only the focus label and physically centered
   timer; controls remain temporary; Reduced Motion disables only colon blinking.
3. Confirm Fixed work and shutdown never move without their explicit unlock/change
   confirmation, and no Recovery, Break, or Shutdown path starts another focus session.
