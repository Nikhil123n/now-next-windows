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
- [Testing strategy](testing/README.md) — verification layers and required scenarios.

## Current phase

Prompt 1 is a specification-and-harness phase. No application solution or implementation
exists. Prompt 2 will create the approved three-project solution and make all .NET CI
steps mandatory.
