# NOW/NEXT — Features Approved for the Windows Rebuild

**Document purpose:** This is the authoritative list of product capabilities approved for the fresh Windows-only rebuild. A feature may be implemented only when it appears here or is first approved through an explicit product decision.

**Product goal:** On one dedicated Windows device, NOW/NEXT should always make the current task obvious, measure focused time accurately, support deliberate transitions, preserve unfinished context, and keep the rest of the day realistic.

## Fixed product boundaries

- Windows 11 only.
- One user and one device.
- Local-first and fully functional without an internet connection.
- No account, cloud backend, synchronization, or external API is required.
- C#, .NET 10 LTS, WinUI 3, Windows App SDK, and SQLite.
- Functional correctness comes before visual polish.
- The active focus screen contains only the task focus label and timer unless the user intentionally reveals controls.
- The timer engine, session state, and recovery logic are authoritative outside the view layer.
- Every schedule change is visible, explainable, reversible where practical, and user-approved.
- The application protects fixed commitments and the planned end of the workday.

---

## P0 — Functional prototype required for real daily use

### 1. Today planning

The application supports a single working day. The user can create, edit, delete, and reorder today’s tasks. Each task stores:

- Full task title.
- Short focus label.
- Definition of done.
- First physical action.
- Planned start time.
- Planned session duration.
- Timing mode: count-up or countdown.
- Schedule type: Fixed or Flexible.
- Importance: Normal or Important.
- Completion, parked, deferred, or active state.

The Today screen shows a simple chronological schedule. It is not a project-management backlog.

### 2. Count-up and countdown focus timers

Both timing modes are approved.

**Count-up mode**

- Starts at `00:00`.
- Counts upward toward the approved session limit.
- At the limit, the user may finish, land, park, extend, or continue into overtime.
- Overtime is displayed with a leading plus sign, such as `+00:01`.

**Countdown mode**

- Starts at the approved session duration.
- Counts down to `00:00`.
- At zero, the same boundary choices appear.
- Continued work is displayed as positive overtime, such as `+00:01`.

The selected mode is saved per task or session. Count-up remains the recommended default, but countdown is a first-class mode rather than a hidden compatibility option.

### 3. Focus-screen presentation

During an active focus session, the normal screen displays only:

- The short task focus label at the top center in medium-sized type.
- A very large timer in the physical center.
- A blinking colon in the timer.

The blinking colon remains part of the product. It should blink at a calm one-second rhythm without shifting layout. When Windows Reduced Motion is enabled, the application may disable the blink automatically for accessibility while preserving it under normal settings.

The focus screen does not display progress bars, daily statistics, motivational messages, AI controls, the next task, a backlog, or permanent buttons.

### 4. Focus-session controls

The user can:

- Start.
- Pause.
- Resume.
- Finish.
- Park.
- Request a five-minute Landing period.
- Extend by 5, 10, 15, or a custom number of minutes.
- Reveal and hide temporary controls.
- Use keyboard and touch input.

Controls remain hidden during normal focus and appear only through an intentional interaction. Automatic task switching is not allowed.

### 5. Boundary cues and overtime

The application provides:

- An optional calm cue shortly before the planned limit.
- A calm cue when the limit is reached.
- An explicit Limit Reached state.
- Overtime that records reality rather than pretending the session stopped.
- No automatic five-minute extension.
- No pressure-oriented “hustle” language.

### 6. Landing

Landing is an optional five-minute count-up period used to finish a clean unit of work and preserve context.

The user can leave Landing by:

- Marking the task done.
- Parking it with a next physical action.
- Extending the focus session.

Landing does not automatically run after every task.

### 7. Parking and the minimal Context Capsule

Parking an unfinished task requires a next physical action unless the user explicitly abandons the task.

The prototype Context Capsule contains:

- Next physical action.
- Optional short note.
- Timestamp.
- Link to the task and last session.

When the task resumes, the next physical action is shown before the timer starts.

### 8. Breaks

The default break duration is five minutes and is configurable. Breaks count upward from `00:00`.

The screen presents one prompt at a time from a small built-in set:

- Look at something far away.
- Take a sip of water.
- Relax the jaw.
- Release the shoulders.
- Stand.
- Walk briefly.
- A user-selected movement such as pushups.

During the final portion of the break, the application shows the next task and its next physical action. The next focus session begins only after confirmation.

### 9. Today schedule panel

An in-application rail or button opens the Today schedule. It must not rely on the Windows right-edge system gesture.

The panel distinguishes:

- Completed.
- Current.
- Fixed.
- Flexible.
- Parked.
- Deferred.
- At risk.

It can show planned versus actual duration after work has occurred. It remains visually simple.

### 10. Deterministic schedule repair

When work overruns or the day no longer fits, the application proposes a repair using simple deterministic rules:

1. Preserve all Fixed tasks.
2. Preserve the planned shutdown time unless the user explicitly unlocks it.
3. Consume available buffer first.
4. Move later Flexible tasks.
5. When the remaining work still does not fit, suggest one Flexible task to defer.
6. Show the revised finishing time and every affected task.
7. Apply nothing until the user approves.

The latest accepted repair can be undone during the current day.

### 11. Recovery

The application recovers safely after:

- Application crash.
- Windows restart.
- Device sleep.
- Device resume.
- A long absence.
- An interrupted focus, Landing, or Break state.

Suspended time is never silently counted as focused work. On return, the user can:

- Resume from committed elapsed time.
- Include the away time.
- End the interrupted session.
- Rebuild the remaining day.

Recovery shows the next Fixed commitment and the time realistically available before it.

### 12. Local persistence

SQLite stores:

- Tasks.
- Today plan.
- Schedule order.
- Focus sessions.
- Pauses.
- Timing mode.
- Planned duration.
- Actual active duration.
- Overtime.
- Landing and break history.
- Context Capsules.
- Schedule repairs.
- Shutdown records.
- Application settings.

Local writes required for recovery are committed before the interface reports success.

### 13. Session history

The prototype provides a simple local history view with:

- Task.
- Timing mode.
- Planned duration.
- Actual active duration.
- Overtime.
- Completion or parked result.
- Date and start time.

It does not calculate a universal productivity score.

### 14. Shutdown

The user can intentionally close the workday.

Shutdown shows:

- Whether the Daily Win was completed, when one was selected.
- Tasks completed.
- Tasks deliberately deferred.
- Planned versus actual time.
- The first physical action for the next unfinished important task.

After confirmation, the application releases keep-awake behavior and enters a resting state or closes according to settings.

---

## P1 — Approved immediately after the P0 workflow survives real use

These features are approved but should not interrupt completion of P0:

- One-line interruption capture that returns immediately to focus.
- Optional Daily Win selection.
- Visible available buffer.
- Undo latest schedule repair.
- Local backup, export, and restore.
- Windows launch-at-sign-in setting.
- Keep-display-awake setting during configured working periods.
- Basic local diagnostic log with no task content unless the user exports it knowingly.
- Tomorrow preview limited to unfinished or explicitly scheduled tasks.
- User-editable break prompt selection.
- Simple sound and cue settings.
- Manual data reset.

---

## Required product journey

The rebuild is not considered functionally complete until this entire journey works on the Surface:

`Plan today → start a task → use count-up or countdown → pause/resume → reach the limit → enter overtime or choose Landing/Done/Park/Extend → take a break → return using the saved next action → repair the day when necessary → recover after restart or sleep → close the workday.`

## Definition of prototype success

The prototype is successful when it can be used for at least 10–14 real working days without losing session state, inventing focused time, silently changing Fixed commitments, or requiring cloud availability.
