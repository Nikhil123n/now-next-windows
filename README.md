# NOW/NEXT for Windows

NOW/NEXT is a private, local-only Windows 11 day-planning and focus application for one
user on one Surface. This repository is the new source of truth and currently contains
the Prompt 3 Today domain and local SQLite foundation behind a plain packaged shell.

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
  reorder, and load operations.
- Packaged application shell: initializes local storage, then displays
  `NOW/NEXT prototype is ready.`
- License: proprietary; see [LICENSE](LICENSE).

See [the documentation index](docs/README.md) and [contribution guide](CONTRIBUTING.md)
for the next steps and definition of done.
