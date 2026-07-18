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
- [Timer invariants](timer-invariants.md) — authoritative elapsed-time, transition,
  boundary, and recovery rules.
- [SQLite schema](sqlite-schema.md) — current migration and retention contract.
- [Testing strategy](testing/README.md) — verification layers and required scenarios.

## Current phase

Prompt 4 adds the pure authoritative focus-session state machine and one durable recovery
checkpoint to the existing Today model and App-owned SQLite storage. The packaged app
still shows only its readiness shell; task/timer UI, schedule repair, session history,
and deferred features remain unimplemented.
