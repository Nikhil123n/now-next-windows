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

## Current vertical-slice boundary

This phase connects the existing Today model, App-owned SQLite store, and authoritative
focus-session engine to a visually plain WinUI flow. It includes today's ordered task
list and approved editor fields, explicit Fixed/Flexible status, Start, both timer modes,
transient focus controls, five-minute Landing, extension, completion, parking with a
durable Context Capsule, explicit abandonment, bounded configurable Breaks, confirmed
return, explicit restart/suspension/long-absence Recovery Mode, one deterministic
same-day repair proposal with transactional acceptance and safe latest-repair undo,
protected daily shutdown settings, durable work totals, explicit Shutdown, and a
restart-safe resting state.

The current Windows dependability slice adds package-local application-data paths,
user-controlled display keep-awake during actively accruing sessions, launch at sign-in,
Reduced Motion and optional full-screen startup preferences, serialized suspend/resume
recovery, privacy-safe local diagnostics, and validated local backup/export/restore/reset.
It does not add a background service, prevent explicit sleep/lid/power actions, or move
session authority out of Core.

The release-candidate qualification layer adds deterministic journey/restart/migration/
data-safety coverage, Windows measurement and recovery commands, a locally self-signed
owner-only MSIX, and recorded manual Surface checks. These are verification and
distribution artifacts only; they do not expand product scope or establish production
signing, Store publishing, automatic updates, telemetry, or support for another device.

The UI is not a general task manager and the DispatcherQueue refresh only requests Core
projections and durability checkpoints; it never owns elapsed time. This phase does not
add a background service, wellness recommendation engine, independent health clocks,
exercise scoring, adaptive or additional focus modes, optimizer-generated schedule
alternatives, a visible buffer editor, general history UI, voice notes, attachments,
recurrence, categories, dependencies, rich notes, AI, calendar integration, multi-day
backlog, themes, custom icons, or visual polish.
