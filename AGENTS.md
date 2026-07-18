# AGENTS.md

NOW/NEXT is a private, local-only Windows 11 application for one user on one Surface.
This repository is the source of truth; do not reuse implementation from older versions.

## Read first

1. [PRODUCT.md](PRODUCT.md) — purpose, users, workflow, and invariants.
2. [SCOPE.md](SCOPE.md) — current boundaries and prohibited work.
3. [ARCHITECTURE.md](ARCHITECTURE.md) — approved stack and project design.
4. [FEATURES_FORWARD.md](FEATURES_FORWARD.md) — authoritative approved capabilities.
5. [FEATURES_DEFERRED_OR_REMOVED.md](FEATURES_DEFERRED_OR_REMOVED.md) — authoritative exclusions and evidence gates.
6. [Documentation index](docs/README.md) — decisions, plans, and testing guidance.

Do not shorten, reinterpret, or weaken either feature register. Count-up and countdown
are first-class modes. The timer colon blinks once per second unless Windows Reduced
Motion calls for a static colon.

## Non-negotiable boundaries

- Windows 11, one user, one device, and fully useful offline.
- No account, backend, cloud sync, calendar integration, AI, or telemetry service.
- Never silently move a Fixed commitment or the planned shutdown time.
- Never silently count sleep, suspension, or an unobserved absence as focused time.
- Schedule repairs are deterministic, explained, previewed, and approved before apply.
- The focus view normally contains only the focus label and timer.
- Do not scaffold application projects until Prompt 2.

## Approved implementation shape

- C# 14, .NET 10 LTS, WinUI 3, current pinned Windows App SDK, and SQLite.
- At most two production projects: `NowNext.App` and `NowNext.Core`.
- One MSTest project: `NowNext.Core.Tests`.
- Direct parameterized SQL and explicit migrations; no generic repository abstraction.
- Manual composition or built-in .NET facilities only; no third-party DI framework.
- No mediator, AutoMapper, Entity Framework, WebView, or future-platform adapters.

## Working rules

- For multi-step work, copy [the plan template](docs/plans/PLAN_TEMPLATE.md).
- Record lasting technical choices in [docs/decisions](docs/decisions/README.md).
- Keep changes surgical and tie every dependency to a current requirement.
- Preserve committed migrations; add a new migration rather than rewriting history.
- Use `TimeProvider` and persisted checkpoints for testable, recoverable timing.
- Follow [.editorconfig](.editorconfig); nullable warnings and analyzer warnings are errors.
- Do not log task content unless the user explicitly exports it.

## Verification

Prompt 1 repository checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-Repository.ps1
```

After Prompt 2 creates `NowNext.slnx`:

```powershell
dotnet restore .\NowNext.slnx --locked-mode
dotnet format .\NowNext.slnx --verify-no-changes --no-restore
dotnet build .\NowNext.slnx --configuration Release --no-restore -warnaserror
dotnet test --solution .\NowNext.slnx --configuration Release --no-build --results-directory .\TestResults --report-trx
```

The definition of done is in [CONTRIBUTING.md](CONTRIBUTING.md). Report checks actually
run, any skipped checks, and remaining risk.
