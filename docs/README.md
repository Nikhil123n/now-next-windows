# Documentation Index

The product has two authoritative scope registers. Read both in full before planning or
implementing a feature:

- [Features approved for the Windows rebuild](../FEATURES_FORWARD.md)
- [Deferred and removed feature register](../FEATURES_DEFERRED_OR_REMOVED.md)

Do not reinterpret, shorten, or weaken their decisions. In particular, count-up and
countdown remain first-class modes, and the one-second blinking timer colon remains
approved subject to Windows Reduced Motion accessibility behavior.

## Product and engineering maps

- [Product](../PRODUCT.md) — purpose, journey, and invariants.
- [Scope](../SCOPE.md) — boundaries and change control.
- [Architecture](../ARCHITECTURE.md) — stack, project boundaries, timing, and storage.
- [Agent guide](../AGENTS.md) — short entry point for future Codex sessions.
- [Contribution guide](../CONTRIBUTING.md) — workflow and definition of done.

## Working documentation

- [Decision records](decisions/README.md) — durable architectural choices.
- [Plan template](plans/PLAN_TEMPLATE.md) — required shape for multi-step work.
- [Prompt 2 scaffold plan](plans/prompt-2-application-scaffold.md) — packaged shell and
  shared verification implementation.
- [Prompt 3 domain and SQLite plan](plans/prompt-3-today-domain-and-sqlite.md) — Today
  model, local persistence, and verification record.
- [Prompt 4 authoritative timer plan](plans/prompt-4-authoritative-timer-state-machine.md)
  — completed implementation scope and verification record.
- [Prompt 5 vertical-slice plan](plans/prompt-5-today-focus-vertical-slice.md) — Today
  planning and the minimal authoritative Focus experience.
- [Timer invariants](timer-invariants.md) — authoritative elapsed-time, transition,
  boundary, and recovery rules.
- [SQLite schema](sqlite-schema.md) — current migration and retention contract.
- [Testing strategy](testing/README.md) — verification layers and required scenarios.
- [Prompt 5 manual test script](testing/prompt-5-manual-test-script.md) — packaged WinUI,
  input, recovery, and accessibility checks that are not reliable in the CLI test host.

## Current phase

The current vertical slice connects the existing Today model, App-owned SQLite storage,
and authoritative session engine to plain Today and Focus screens. Schedule repair,
Break UI, session history, Context Capsules, visual polish, and deferred features remain
unimplemented.
