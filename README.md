# NOW/NEXT for Windows

NOW/NEXT is a private, local-only Windows 11 day-planning and focus application for one
user on one Surface. This repository is the new source of truth and currently contains
the Today domain and local SQLite foundation plus the Prompt 4 authoritative focus-session
and recovery foundation behind a plain packaged shell.

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

## Current status

- Product and engineering decisions: specified.
- Repository policy and Windows CI: configured.
- Today domain: immutable task values and validated single-day ordering.
- Local persistence: versioned App-owned SQLite for today's create, edit, soft delete,
  reorder, and load operations, plus one durable current-session checkpoint.
- Focus sessions: pure Core transitions for both timer modes, limits, overtime, Landing,
  extension, completion, parking, Break, recovery, and day closure. Elapsed work comes
  from monotonic time rather than UI refreshes.
- Packaged application shell: initializes local storage, then displays
  `NOW/NEXT prototype is ready.` It does not yet contain task or timer UI.
- License: proprietary; see [LICENSE](LICENSE).

See [the documentation index](docs/README.md) and [contribution guide](CONTRIBUTING.md)
for the next steps and definition of done.
