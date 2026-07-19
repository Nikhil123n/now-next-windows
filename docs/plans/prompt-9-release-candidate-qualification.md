# Plan: Qualify the Windows prototype release candidate

- Owner: Codex
- Status: Completed
- Updated: 2026-07-19
- Related decision/issue: none; qualification does not change architecture

## Goal and observable success

Qualify the existing plain P0 vertical slice as a locally installable prototype for the
owner's Surface. Demonstrate the complete Today-to-Shutdown journey through deterministic
automated coverage and a recorded packaged manual script, prove committed state survives
restart and practical forced termination, measure the available Windows device without
invented thresholds, and produce a development-signed MSIX that remains fully useful
offline.

## Constraints and assumptions

- Preserve every invariant in [the approved feature register](../../FEATURES_FORWARD.md)
  and every exclusion in
  [the deferred/removed register](../../FEATURES_DEFERRED_OR_REMOVED.md).
- Add no product capability, visual polish, project, runtime dependency, schema migration,
  updater, Store publishing, production-signing infrastructure, telemetry, or cloud path.
- The owner Surface already has Windows Developer Mode enabled. Use a local self-signed
  code-signing certificate for this one-device prototype, import only its public half
  into `LocalMachine\\TrustedPeople`, and never trust it as a root. This is qualification
  tooling, not the deferred production-signing infrastructure.
- Automated timing uses `TimeProvider` and no sleeps. Hardware observations report the
  measured values and conditions but impose no invented pass/fail numbers.
- WinUI interaction, touch, real suspend/lid behavior, and long battery use remain manual
  where the existing three-project contract cannot automate them reliably.

## Steps

1. Add deterministic release-candidate tests for the persisted P0 journey, every active
   restart phase, long and repeated fake-clock transitions, all existing migration
   baselines, practical forced process termination, keyboard accessibility, Reduced
   Motion, and backup/restore retention.
2. Add repository-only PowerShell commands that build a locally signed MSIX, install and
   smoke-launch it, force-terminate and relaunch it, and record cold-start, idle CPU,
   idle memory, and bounded long-run observations.
3. Run the canonical verification command, create and install the package, exercise the
   packaged flow and data safeguards where this Windows environment permits, and fix any
   correctness or reliability failure found.
4. Record prerequisites, installation, data location, backup/recovery, uninstall behavior,
   exact tool/device versions, measured results, manual results, and explicit limitations.

## Verification

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-PrototypePackage.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Measure-Prototype.ps1`
- Install the generated MSIX with its public development certificate, launch offline,
  complete the Prompt 9 manual journey, force-terminate during an active checkpoint,
  relaunch into Recovery, and uninstall while checking documented retained-data behavior.
- Treat the known successful WinUI `dotnet format` workspace diagnostic as informational
  only when the process exits zero and leaves source formatted.

## Risks and rollback

The main risks are tests that simulate behavior without exercising packaged WinUI,
measurement noise, and package replacement affecting the current LocalState database.
Keep UI/hardware evidence explicitly manual, record raw observations rather than
  thresholds, require an explicit elevated trust/install step, export/backup before
  package replacement, and retain the same package identity. Qualification-only tests,
scripts, and documentation can be reverted without a database downgrade because no schema
or production behavior changes.

## Qualification record

### Automated evidence

- Canonical command: passed on 2026-07-19. Locked restore, formatting verification,
  Release build, and Microsoft.Testing.Platform completed; build reported zero warnings
  and zero errors; 183 tests passed with no failures or skips.
- The release-candidate additions cover the persisted P0 journey, schema starts 0 through
  4, every durable active restart phase, a killed child test process, 14-hour fake-clock
  sessions, 1,000 repeated boundary and pause/resume transitions, keyboard command and
  Reduced Motion policies, and backup/restore of Context Capsule, settings, and active
  recovery state.
- The known WinUI workspace diagnostic appeared during successful formatting and exited
  zero. It did not leave source changes or produce a compiler warning.
- Packaged .NET CLI launch passed. UI Automation found the `NOW/NEXT` window, Today
  heading, readiness text, clean-plan message, Reduced Motion status, and the newly
  created package LocalState database.
- The package build produced one x64 MSIX signed by `CN=NowNext Development`; its embedded
  signer thumbprint matched the exported public certificate. The install script's
  non-elevated refusal and all qualification-script parse checks passed.
- Final MSIX SHA-256:
  `2D710BDB3AF1369D0AE6E0A36C3B14FF6D777EFBDCA368BD5715DE2EAD80C34B`;
  public-certificate SHA-256:
  `E066089FDE3FE44BEC16DF7E0E103EDFEE5A877A97330232B938E6AA0B529061`.

### Environment and tools

- Available device: Dell Inc. Dell G15 5530, 16 GB RAM, 1920 x 1080 at 100% scaling.
  No Surface was available to this process, so Surface touch, lid, Modern Standby, and
  battery-use qualification remain unrun hardware checks.
- Windows 11 Home `10.0.26200` x64; Windows Developer Mode enabled; Balanced power plan;
  battery present at 100% and AC online at the start of measurement.
- .NET SDK `10.0.302` (`35b593bebf`), MSBuild `18.6.11`, .NET host/runtime `10.0.10`,
  PowerShell `5.1.26100.8875`, Git `2.47.0.windows.1`, Windows SDK MakeAppx/SignTool
  `10.0.26100.7705`, and Windows App Runtime `2.3.1.0` x64.
- Visual Studio's XAML/MSIX designers and debugger integration were not used or qualified.
  The documented .NET CLI remains the verified development path.

### Device measurement

The completed record is
`artifacts/qualification/prototype-measurement-20260719-045250.json` (local, ignored):

- Cold start to the UI Automation-visible main window: 951.3 ms.
- Thirty-second idle sample: 0 CPU-seconds and 0% normalized CPU; 158.70 MiB working set;
  81.59 MiB private memory.
- Long run: 3,600.8 seconds, 120 of 120 samples responsive, 0.5625 processor-seconds,
  maximum 160.18 MiB working set and 83.77 MiB private memory; process exit code absent.
- Conditions: AC online, battery reported 100%, Balanced plan, 1920 x 1080 primary display.

These are raw observations, not release thresholds. An initial observation was invalidated
when the qualification process removed the live CLI-registered package's CurrentUser
trust entry during sampling; Windows then tore down its AppX container without a crash or
WER record. The harness was corrected to retain partial output and exit codes, the
temporary non-root trust entry was held for the valid rerun, and the full hour completed.
Afterward the temporary CurrentUser Trusted People entry was removed; no test certificate
remains in any Root store or LocalMachine store.

### Manual and hardware status

| Qualification area | Result | Evidence or limitation |
| --- | --- | --- |
| Clean package data and plain launch | Pass through packaged CLI registration | Today opened empty, readiness and LocalState database were visible. |
| Elevated MSIX installation | Not run | UAC consent was unavailable to the automation session. The signed artifact and guarded elevated installer were produced. |
| Complete P0 state and persistence journey | Pass automated; manual not run | Deterministic temp-database journey covers both modes through Shutdown and restart. |
| Forced termination and restart phases | Pass automated; packaged active-state matrix not run | Killed child process retained its committed checkpoint; all durable phases restored without away time. |
| Keyboard and Reduced Motion | Pass contract/policy; physical input not run | Commands and colon-only policy are automated; live shell reported Reduced Motion enabled. |
| Touch, lid, sleep/resume, display wake, battery session | Not run | The available device was not a Surface and no physical hardware actions were performed. |
| Backup/restore and private diagnostics | Pass automated; UI maintenance path not run | Validated restore retained capsule/settings/recovery; live log contained controlled event/result fields only. |
| Offline launch | Not run by disconnecting the device | Production paths have no backend/cloud dependency, but network state was not changed during this task. |
| Uninstall and external-export retention | Not run | Destructive uninstall requires owner confirmation after an external backup. Documented behavior remains a manual check. |
