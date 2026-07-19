# Prompt 8 Surface hardware test

Run this script on the target Surface with a packaged Release build after the canonical
verification command. Use disposable task text for reset/restore checks. Record the
Windows build, Surface model, display resolution/scaling, power mode, battery/AC state,
Reduced Motion state, and each result. Do not include real task titles, notes, or Context
Capsules in shared evidence.

## Launch, touch, and Windows preferences

1. Launch NOW/NEXT normally. Confirm Today is readable at the Surface landscape
   resolution and every Windows/data control is reachable by touch and keyboard.
2. Enable **Start full screen next launch**, close the app, and relaunch. Confirm Today
   opens full screen. Disable it, relaunch, and confirm a normal window. Focus and Break
   should still use their existing full-screen presentation regardless of this startup
   preference.
3. Turn Windows **Animation effects** off, enter Focus, and confirm the colon remains
   visible and static. Turn Animation effects on and confirm the calm one-second blink
   returns without moving the timer.
4. Enable **Launch at sign-in**, sign out and back in, and confirm one NOW/NEXT instance
   launches. Disable it and repeat. Disable the startup entry in Task Manager, relaunch
   NOW/NEXT, and confirm the app reports that Windows owns re-enabling it rather than
   overriding the user's choice.

## Display, explicit sleep, lid, and resume

1. With **Keep display awake** off, start a short focus session and verify the Surface
   follows its configured idle-display timeout.
2. Enable the setting, start/resume Focus, and leave the device untouched beyond that
   timeout. Confirm the display stays on. Pause; confirm the display may idle normally.
3. Resume, then use the Windows power menu to choose **Sleep**. The Surface must sleep
   promptly. Wake it and confirm Recovery Mode appears with only committed pre-sleep
   focus time; the session must not resume automatically.
4. Repeat with the Surface Type Cover/lid close action configured to sleep. Confirm lid
   close is not blocked, the display request is released, and opening/waking routes to
   Recovery Mode without counting the closed-lid interval.
5. Repeat the sleep/resume check once during Landing and once during Break. Confirm the
   saved phase and committed elapsed value return, with away time excluded unless it is
   explicitly included.
6. While focus is running, close the app normally and relaunch. Confirm the final
   checkpoint is durable and the app requires a Recovery choice. Then confirm explicit
   Windows restart/shutdown is not delayed by NOW/NEXT and the next launch restores the
   last committed checkpoint.

## Battery and long-running session

1. Disconnect AC power and run a focus session on battery with keep-awake disabled.
   Confirm timer projections remain authoritative through display dim/off and normal
   foreground use.
2. Enable keep-awake on battery for one intentional session. Confirm it is active only
   while Focus, overtime, Landing, or Break is accruing, then releases on pause,
   Recovery, Done/Park, Shutdown, and app exit. Note expected battery impact.
3. Run one session for at least 60 minutes, cross the planned limit, and continue into
   overtime. Include ordinary delayed rendering/window movement. Confirm elapsed and
   overtime remain correct, then sleep/wake and verify no time during sleep is invented.

## Backup, export, restore, reset, and diagnostics

1. Create two disposable tasks and a committed session checkpoint. Select **Backup** and
   confirm a timestamped validated database appears under the package LocalState
   `Backups` directory.
2. Change the disposable plan, choose **Restore**, select that backup, review the
   confirmation, and restore. Confirm the earlier tasks, order, settings, and committed
   session state return exactly. If the restored checkpoint was active, Recovery Mode
   must appear rather than silently resuming.
3. Select **Export**, choose a local folder through the Windows picker, and confirm the
   exported `.db` can be selected for a validated restore. Cancel both pickers once and
   confirm cancellation changes nothing.
4. Copy a non-database file with a `.db` extension and attempt restore. Confirm it is
   rejected and current local data remains intact.
5. Inspect `Diagnostics\now-next.log.jsonl`. Confirm entries contain controlled event,
   result, UTC timestamp, and optional exception-type values only. Search for the exact
   disposable task title, note, next action, and a Context Capsule phrase; none may be
   present.
6. With only disposable data present, select **Reset data**. Confirm the primary action
   stays disabled until the explicit acknowledgement is checked. Complete reset and
   verify tasks, sessions, settings, backups, exports, and old diagnostics are gone,
   launch-at-sign-in is off unless Windows policy owns it, and a fresh migrated database
   opens normally.

## Result record

Record pass/fail for every numbered item and any Surface-specific limitation. A failed
sleep, lid, restart, restore, or keep-awake-release check blocks dependable daily use.
Visual Studio XAML/MSIX designer and debugger integration remain outside this milestone;
the verified development path is the documented .NET CLI workflow.
