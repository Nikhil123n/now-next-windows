# Prompt 5 Manual Test Script

Use this script on the target Windows 11 Surface or a Windows 11 machine with the package
runtime prerequisites documented in [the architecture](../../ARCHITECTURE.md). It covers
WinUI interaction that is not reliable in the current command-line MTP host.

## Preparation

1. Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1`.
2. Close NOW/NEXT. For a clean plan, run
   `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Database-Dev.ps1 -Reset -ConfirmReset`.
3. Launch with
   `dotnet run --project .\src\NowNext.App\NowNext.App.csproj --configuration Release --no-build`.
4. Confirm the Today screen opens without network access and the window remains usable in
   the Surface's native landscape resolution.

## Today planning

1. Add a Flexible count-up task and a Fixed countdown task. For each, enter the full
   title, short focus label, definition of done, first physical action, planned start,
   positive duration, timer mode, schedule type, and importance.
2. Confirm blank full titles, blank focus labels, missing planned times, and zero or
   negative durations produce a specific inline failure and do not create a task.
3. Confirm each row shows planned time, full title, focus label, duration, timer mode, and
   an explicit `Fixed — protected time` or `Flexible — movable time` label.
4. Edit both tasks and relaunch the app; confirm every value is restored. Move each task
   earlier and later and confirm order survives relaunch. Delete one task, cancel once,
   then confirm deletion and verify it leaves Today.
5. Using touch and then keyboard-only navigation, reach Add, Start, Earlier, Later, Edit,
   and Delete. Confirm the default focus indicator is visible and every target is easy to
   activate.

## Focus and authoritative timing

1. Set the count-up task duration to one minute and choose Start. Confirm the app enters
   full screen. Without interacting, verify the only visible content is the medium short
   focus label at top center and the large timer in the physical center.
2. Confirm the timer begins at `00:00`, uses fixed-width numerals, and advances correctly
   even after covering the app or delaying interaction. Confirm the colon changes opacity
   on a calm one-second rhythm without moving any timer digit.
3. Move the pointer or tap once. Confirm controls appear. Press Escape or choose Hide,
   confirm they disappear, reveal them again, then wait at least six seconds and confirm
   they disappear without changing elapsed time.
4. Press Space to pause, wait, and press Space to resume. Confirm paused time is excluded.
   Repeat through the touch buttons. Use `F`, `P`, `L`, `O`, and `E` to confirm keyboard
   access to Finish, Park, Landing, continued overtime, and custom extension when those
   commands are legal.
5. Reach the one-minute limit. Confirm continued work displays positive overtime with a
   leading `+`. Reveal controls and exercise Landing; confirm it counts up from zero.
   Exercise 5-, 10-, 15-, and custom-minute extension paths and confirm focus resumes
   under the newly approved limit.
6. Repeat with the countdown task. Confirm it begins at the planned duration, reaches
   `00:00`, and then uses the same positive overtime and decision paths as count-up.
7. Finish one session and confirm Today restores with its task state. Park another; confirm
   a blank next physical action is refused and a nonblank action completes parking.

## Recovery and Windows accessibility

1. Start focus, work for at least six seconds, and close the window. Relaunch. Confirm the
   saved focus label/timer are restored with a required recovery choice and no time away
   has been added. Choose Resume excluding time away and confirm the timer continues from
   committed time.
2. Repeat, explicitly include a small known amount no greater than the displayed/observed
   absence, and confirm only that amount is credited. Confirm an excessive amount is
   rejected without changing the saved session.
3. If the device supports it, repeat through sleep or Modern Standby and confirm the same
   explicit recovery behavior.
4. In Windows Accessibility settings, turn Animation effects off. Relaunch Focus and
   confirm the colon remains visible and static while the timer still updates. Turn the
   setting back on and confirm blinking returns.
5. Check keyboard focus, Narrator names, 200% text scaling, and a Windows high-contrast
   theme. Confirm the Today editor remains operable, Focus timer remains centered and
   legible, controls do not clip at landscape resolution, and no meaning relies on color.

Record Windows version, display resolution/scaling, input methods, Reduced Motion state,
and any failed step with a screenshot. Do not include task content in diagnostics shared
outside the local machine.
