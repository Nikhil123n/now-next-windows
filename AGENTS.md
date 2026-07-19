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
- The current vertical slice includes plain Today planning and the minimal Focus view on
  top of the authoritative Core session engine, durable Context Capsules, bounded Breaks,
  recovery checkpoints, deterministic same-day repair, and durable Shutdown.
- Windows integration is App-owned. Display keep-awake is user-controlled, applies only
  while active session time accrues, and must release for pause, Recovery, suspension,
  day close, and exit without blocking explicit sleep, lid, power, or shutdown actions.
- Backup/export/restore/reset must use validated SQLite operations and exact LocalState
  path checks. Default diagnostics accept no user-authored task or Context Capsule text.
- The current release-candidate phase adds qualification tests, measurement commands,
  and local development packaging only. It adds no product behavior, production signing,
  updater, Store path, telemetry, or visual polish.
- Do not infer permission for visual polish, background services, optimizer menus,
  general history UI, wellness engines, multi-day planning, or another focus mode from
  this slice.

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
- Keep session transitions immutable and explicit; serialize App persistence operations
  with the smallest built-in asynchronous primitive that meets the current requirement.
- Follow [.editorconfig](.editorconfig); nullable warnings and analyzer warnings are errors.
- Do not log task content unless the user explicitly exports it.

## Verification

Run the canonical local and CI verification command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1
```

The script validates repository policy, performs a locked restore, verifies formatting,
builds Release with warnings as errors, and runs tests through Microsoft.Testing.Platform.

The definition of done is in [CONTRIBUTING.md](CONTRIBUTING.md). Report checks actually
run, any skipped checks, and remaining risk.
