# Prompt 6 Manual Test Script

Use this script on Windows 11 after the canonical verification command. It covers WinUI,
packaging, pointer/touch/keyboard, restart, and accessibility behavior that the current
CLI test host cannot reliably automate.

## Setup

1. If test data may be discarded, reset only the prototype database with
   `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Database-Dev.ps1 -Reset -ConfirmReset`.
2. Launch the packaged Release build with
   `dotnet run --project .\src\NowNext.App\NowNext.App.csproj --configuration Release --no-build`.
3. Add two short tasks. Give the first a one-minute duration and make the second task's
   first physical action easy to recognize.

## Boundary, Landing, and outcomes

1. Start the first task and allow it to reach the limit without interacting with the UI.
   Confirm the authoritative timer reaches the boundary even if rendering was delayed.
2. Reveal controls by pointer movement, tap, and keyboard. Confirm Done, Landing, Park,
   Continue overtime, and Extend are reachable, every target is usable by touch, and
   Escape hides the temporary controls.
3. Choose Landing. Confirm it counts upward as focus time, stops at five minutes, and
   does not extend or finish automatically. Repeat with a development-duration task if a
   five-minute manual wait is impractical.
4. Choose Done. Confirm the app offers one optional Break or a return to Today; it does
   not start another task.

## Parking, capsule, and explicit abandon

1. Park a task. Leave the next physical action blank and confirm Park is refused with a
   clear validation message.
2. Enter a next physical action and optional short note, then Park successfully.
3. Return to Today without a Break, select that task again, and confirm the saved next
   action and note are visible before the Start focus confirmation.
4. Repeat the Park flow and choose Abandon task. Confirm a second explicit confirmation
   is required and no next action is demanded for abandonment.

## Break and confirmed return

1. After Done or Park, select a positive Break duration and exactly one prompt from the
   approved list. Test a built-in prompt and a user-selected movement.
2. Confirm Break counts up independently and the Focus duration does not change.
3. During the final minute, confirm the returning task and next physical action appear.
4. At the Break limit, confirm the timer stops and the screen waits at Confirm return.
5. Confirm return. Verify the saved next action appears before focus and that no new
   session starts until Start focus is explicitly chosen.

## Restart recovery

1. Close the app during Landing, relaunch, and confirm only committed Landing/focus time
   is restored with the explicit exclude/include-away recovery choice.
2. Close during Break, wait at least 30 seconds, and relaunch. Confirm the selected prompt,
   Break limit, returning context, and committed Break elapsed are restored. Excluding
   time away must not count the wait; explicitly including a bounded amount may count it.
3. Close at the completed Break boundary, relaunch, and confirm the app still waits for
   return confirmation.
4. Park successfully, close before starting a Break, relaunch, and confirm the capsule is
   retained and the Break/Today choice is recoverable.

## Accessibility and restraint

1. Use only keyboard navigation for the boundary, Park dialog, Break setup, recovery,
   End Break, and pre-focus confirmation. Check visible focus and useful accessible names.
2. With Windows Reduced Motion enabled, confirm the Focus timer colon remains visible and
   static while all timing and controls continue to work.
3. Check high contrast, 200% text scaling, and Surface landscape layout. Content must
   remain readable without clipped decision buttons.
4. Confirm the normal Focus screen contains only the focus label and timer, and the Break
   screen shows one prompt at a time with no progress bar, scoring, statistics, or wellness
   recommendations.
