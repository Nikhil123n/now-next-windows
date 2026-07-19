# NOW/NEXT for Windows

NOW/NEXT is a private, local-only Windows 11 day-planning and focus application for one
user on one Surface. This repository is the new source of truth and currently contains
the Today domain and local SQLite foundation, the authoritative focus-session and
recovery engine, deterministic same-day repair, durable Shutdown, and a plain runnable
Today-to-Focus-to-Break vertical slice with bounded Windows lifecycle and local-data
safety integration.

Start with [AGENTS.md](AGENTS.md), then read [PRODUCT.md](PRODUCT.md),
[SCOPE.md](SCOPE.md), [ARCHITECTURE.md](ARCHITECTURE.md), the authoritative
[approved feature register](FEATURES_FORWARD.md), and the authoritative
[deferred/removed register](FEATURES_DEFERRED_OR_REMOVED.md).

## Verification

From Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1
```

This validates repository policy, restores locked packages, verifies formatting, builds
Release with warnings as errors, and runs the MSTest suite.

Build the owner-only locally installable prototype after verification with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-PrototypePackage.ps1
```

Installation prerequisites, the elevated local-certificate step, data safety, recovery,
and uninstall behavior are in the
[release-candidate guide](docs/release-candidate/README.md).

Launch the packaged application after a Release build with:

```powershell
dotnet run --project .\src\NowNext.App\NowNext.App.csproj --configuration Release --no-build
```

## Current status

- Product and engineering decisions: specified.
- Repository policy and Windows CI: configured.
- Today domain: immutable task values and validated single-day ordering.
- Local persistence: versioned App-owned SQLite for today's create, edit, soft delete,
  reorder, and load operations, plus a durable current-session checkpoint, Context
  Capsules, Break defaults, schedule revisions, retained session totals, accepted repair
  audit/undo, and day closure.
- Focus sessions: pure Core transitions for both timer modes, limits, overtime, Landing,
  extension, completion, parking, explicit abandonment, bounded Break, recovery, and day
  closure. Elapsed work comes from monotonic time rather than UI refreshes.
- Packaged application: a plain Today screen supports approved-field editing, deletion,
  ordering, and Start. The full-screen Focus view uses the authoritative engine for both
  timer modes, transient controls, overtime, Landing, extension, parking, completion,
  explicit restart/suspension recovery, atomic Context Capsule saving, one-prompt Breaks,
  confirmed return without automatic task switching, one explained repair proposal,
  15-minute absence Recovery Mode, explicit Shutdown, and a restart-safe resting state.
- Windows dependability: package-local paths/settings, a user-controlled active-session
  display request, launch at sign-in, Reduced Motion, optional full-screen startup,
  suspend/resume Recovery routing, content-free local diagnostics, and validated local
  backup/export/restore/reset.
- UI automation: deterministic presentation contracts are tested in MSTest; the
  interaction and Windows accessibility cases are recorded in the
  [Prompt 8 Surface hardware test](docs/testing/prompt-8-surface-hardware-test.md).
- Release-candidate qualification: complete persisted-journey, restart-state, every-
  migration-baseline, forced-termination, long fake-clock, repeated-transition,
  accessibility, Reduced Motion, and backup/restore contracts are automated; the
  remaining packaged Surface interactions are recorded in the
  [Prompt 9 qualification script](docs/testing/prompt-9-release-candidate-test.md).
- License: proprietary; see [LICENSE](LICENSE).

See [the documentation index](docs/README.md) and [contribution guide](CONTRIBUTING.md)
for the next steps and definition of done.
