# Contributing

This is a private, single-product repository. Keep changes small, product-led, and easy
for a human or a context-free Codex session to review.

## Before changing anything

1. Read [AGENTS.md](AGENTS.md) and the documents it maps.
2. Confirm the work is approved by [FEATURES_FORWARD.md](FEATURES_FORWARD.md) and is
   not deferred or removed by
   [FEATURES_DEFERRED_OR_REMOVED.md](FEATURES_DEFERRED_OR_REMOVED.md).
3. For multi-step work, create a plan from
   [docs/plans/PLAN_TEMPLATE.md](docs/plans/PLAN_TEMPLATE.md).
4. Add a decision record when a lasting architectural choice changes.

## Dependency policy

Use the framework and standard library first. A new package must solve a current
requirement, support Windows and the pinned .NET version, have an acceptable license,
avoid an unnecessary service, and not duplicate an existing dependency. Pin versions
centrally and commit lock files once projects exist. Dependabot proposals receive the
same review as manual upgrades.

## Coding conventions

- Follow `.editorconfig`; use nullable reference types and treat warnings as errors.
- Prefer explicit domain names, small cohesive types, immutable values, and early returns.
- Keep I/O asynchronous and cancellation-aware; avoid blocking UI-thread work.
- Use `TimeProvider` instead of direct wall-clock calls in domain logic.
- Keep WinUI concerns in App and deterministic behavior in Core.
- Use parameterized SQL and explicit transaction boundaries.
- Do not edit a migration already merged to `main`.
- Comments explain intent or constraints, not syntax.

## Verification

Run the canonical command documented in [AGENTS.md](AGENTS.md) for every change. It
validates repository policy, performs a locked restore, verifies formatting, builds
Release, and runs tests. Add tests for state transitions, both timer modes, boundary
behavior, recovery, schedule repair, persistence, and migrations as applicable.

## Definition of done

A change is done when:

- it implements only approved scope and preserves every product invariant;
- acceptance behavior and failure states are explicit;
- applicable automated tests cover normal, boundary, and recovery cases;
- accessibility behavior is checked for user-visible changes;
- persistence changes include forward migrations and upgrade tests;
- documentation and decisions match the implementation;
- repository validation, formatting, Release build, and tests pass when applicable;
- no task content enters diagnostics, and no cloud or cross-platform dependency appears;
- the pull request states checks run, checks skipped with reasons, and remaining risk.
