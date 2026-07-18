# Scope

[FEATURES_FORWARD.md](FEATURES_FORWARD.md) is the authoritative approved feature
register. [FEATURES_DEFERRED_OR_REMOVED.md](FEATURES_DEFERRED_OR_REMOVED.md) is the
authoritative exclusion and evidence-gate register. This document provides navigation
and engineering boundaries; it does not replace or narrow either register.

## In scope

- A Windows 11 desktop application for one user and one Surface.
- A single working-day plan with Fixed and Flexible tasks.
- Count-up and countdown focus sessions, each with honest positive overtime.
- The minimal focus presentation, including the approved blinking colon and Windows
  Reduced Motion behavior.
- Deliberate limit, Landing, parking, break, recovery, schedule-repair, history, and
  shutdown flows approved in the forward register.
- Local SQLite persistence and local settings.
- P0 before P1; P1 work must not delay proving the complete P0 journey.

## Out of scope

- Accounts, identity, collaboration, multi-device operation, synchronization, or cloud
  backup.
- Backend services, APIs, distributed components, queues, hosted observability, or
  telemetry services.
- Calendar or task-manager integration, hosted AI, web UI, mobile UI, Linux, macOS, or
  cross-platform frameworks and adapters.
- Gamification, pressure mechanics, automatic task switching, and focus-screen clutter.
- Deferred visual polish, analytics, adaptations, metadata, scheduling, integrations,
  distribution, and interaction methods until their written evidence gates are met.

GitHub Actions and Dependabot are repository development automation, not application
runtime dependencies.

## Change control

A capability may be implemented only if it is approved by the forward register. A
deferred or removed capability requires the written reconsideration decision defined in
the exclusion register and corresponding updates to both authoritative files before
implementation begins. Do not infer approval from technical convenience.

## Prompt 4 boundary

This phase adds the pure authoritative focus-session state machine, monotonic active-time
accounting, durable recovery checkpoints, and the narrow App persistence/lifecycle bridge
needed to commit them. It covers count-up, countdown, pause/resume, the planned limit,
positive overtime, five-minute Landing, explicit extension, completion, parking, Break,
recovery, and day closure as domain transitions rather than as screens.

The packaged shell remains plain and has no task or timer UI. This phase does not add a
background service, adaptive or additional focus modes, schedule repair, session-history
table, Context Capsule note, recurrence, categories, dependencies, rich notes, AI,
calendar integration, or multi-day backlog. Those capabilities remain governed by the
authoritative feature registers and their later milestones.
