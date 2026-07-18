# NOW/NEXT for Windows

NOW/NEXT is a private, local-only Windows 11 day-planning and focus application for one
user on one Surface. This repository is the new source of truth and currently contains
the Prompt 1 product specification and development harness only—no application code.

Start with [AGENTS.md](AGENTS.md), then read [PRODUCT.md](PRODUCT.md),
[SCOPE.md](SCOPE.md), [ARCHITECTURE.md](ARCHITECTURE.md), the authoritative
[approved feature register](FEATURES_FORWARD.md), and the authoritative
[deferred/removed register](FEATURES_DEFERRED_OR_REMOVED.md).

## Repository validation

From Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-Repository.ps1
```

This validates the audited repository skills, specification files, internal links,
workflow policies, feature-register fingerprints, and the absence of implementation
artifacts during Prompt 1.

## Current status

- Product and engineering decisions: specified.
- Repository policy and Windows CI: configured.
- Application solution and projects: intentionally not scaffolded until Prompt 2.
- License: proprietary; see [LICENSE](LICENSE).

See [the documentation index](docs/README.md) and [contribution guide](CONTRIBUTING.md)
for the next steps and definition of done.
