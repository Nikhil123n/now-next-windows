# NOW/NEXT for Windows

NOW/NEXT is a private, local-only Windows 11 day-planning and focus application for one
user on one Surface. This repository is the new source of truth and currently contains
the Today domain and local SQLite foundation, the authoritative focus-session and
recovery engine, and a plain runnable Today-to-Focus vertical slice.

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

Launch the packaged application after a Release build with:

```powershell
dotnet run --project .\src\NowNext.App\NowNext.App.csproj --configuration Release --no-build
```

## Current status

- Product and engineering decisions: specified.
- Repository policy and Windows CI: configured.
- Today domain: immutable task values and validated single-day ordering.
- Local persistence: versioned App-owned SQLite for today's create, edit, soft delete,
  reorder, and load operations, plus one durable current-session checkpoint.
- Focus sessions: pure Core transitions for both timer modes, limits, overtime, Landing,
  extension, completion, parking, Break, recovery, and day closure. Elapsed work comes
  from monotonic time rather than UI refreshes.
- Packaged application: a plain Today screen supports approved-field editing, deletion,
  ordering, and Start. The full-screen Focus view uses the authoritative engine for both
  timer modes, transient controls, overtime, Landing, extension, parking, completion,
  and explicit restart/suspension recovery.
- UI automation: deterministic presentation contracts are tested in MSTest; the
  interaction and Windows accessibility cases are recorded in the
  [Prompt 5 manual test script](docs/testing/prompt-5-manual-test-script.md).
- License: proprietary; see [LICENSE](LICENSE).

See [the documentation index](docs/README.md) and [contribution guide](CONTRIBUTING.md)
for the next steps and definition of done.
