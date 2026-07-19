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
- [Prompt 6 Context and Break plan](plans/prompt-6-context-break-journey.md) — durable
  parking context, bounded Breaks, and confirmed return.
- [Prompt 7 repair, Recovery, and Shutdown plan](plans/prompt-7-schedule-recovery-shutdown.md)
  — deterministic repair, durable workday decisions, and verification record.
- [Prompt 8 Windows integration plan](plans/prompt-8-windows-integration-and-data-safety.md)
  — bounded Windows lifecycle behavior, local data safety, and verification record.
- [Prompt 9 release-candidate plan](plans/prompt-9-release-candidate-qualification.md)
  — complete-journey qualification, packaging, measurements, and result record.
- [Timer invariants](timer-invariants.md) — authoritative elapsed-time, transition,
  boundary, and recovery rules.
- [SQLite schema](sqlite-schema.md) — current migration and retention contract.
- [Testing strategy](testing/README.md) — verification layers and required scenarios.
- [Prompt 5 manual test script](testing/prompt-5-manual-test-script.md) — packaged WinUI,
  input, recovery, and accessibility checks that are not reliable in the CLI test host.
- [Prompt 6 manual test script](testing/prompt-6-manual-test-script.md) — Landing, Park,
  Context Capsule, Break, restart, and return-confirmation checks.
- [Prompt 7 manual test script](testing/prompt-7-manual-test-script.md) — protected
  repair, late Recovery, Shutdown, resting-state, and accessibility checks.
- [Prompt 8 Surface hardware test](testing/prompt-8-surface-hardware-test.md) — touch,
  display wake, sleep/resume, lid, autostart, battery, long-session, and data-safety checks.
- [Prompt 9 release-candidate test](testing/prompt-9-release-candidate-test.md) — clean
  install, full P0 journey, restart matrix, accessibility, data safety, and measurements.
- [Release-candidate guide](release-candidate/README.md) — prerequisites, local package
  installation, data location, backup/recovery, known limitations, and uninstall.

## Current phase

The current vertical slice connects the Today model, App-owned SQLite storage, and
authoritative session engine to plain Today, Focus, and Break screens. It durably retains
parking context, provides one deterministic same-day repair, recovers from substantial
absence without invented time, and persists explicit Shutdown. General history UI,
multi-day planning, optimizer menus, background services, cloud backup, remote
diagnostics, visual polish, and deferred features remain unimplemented. The App now adds
bounded Windows lifecycle notifications, active-session display keep-awake, startup and
full-screen preferences, privacy-safe diagnostics, and validated local data maintenance.
The repository now qualifies that existing behavior with deterministic end-to-end state
coverage, a locally installable owner-only package, raw device measurements, and an
explicit manual Surface result record; qualification adds no product feature.
