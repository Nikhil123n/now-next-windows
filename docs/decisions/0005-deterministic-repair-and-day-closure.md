# 0005 — Deterministic repair and durable day closure

- Status: Accepted
- Date: 2026-07-18

## Context

The Today plan now has authoritative task order, focus checkpoints, parking context, and
Break recovery. Same-day disruption still needs an explainable way to protect Fixed
commitments and shutdown, while Recovery and Shutdown need one durable source of truth.
A generic optimizer or application-service layer would obscure the product's ordered
rules and create extension points the single-user prototype does not need.

## Decision

Core owns a pure deterministic repair function. It excludes completed/deferred work,
holds the current authoritative finish and every Fixed interval, places Flexible work in
stable order through available gaps, and recommends at most one deferral using Normal
before Important and latest plan position within that importance. A conflict with
protected time or insufficient benefit from that single deferral produces one
non-acceptable impossible proposal. The function returns its trigger, per-gap buffer,
moves, deferral, finish/overflow, and protected Fixed references.

App persistence owns proposal acceptance and undo. Each day has a nonnegative schedule
revision and an explicitly selected shutdown. Acceptance checks the exact revision and
shutdown used by the proposal, writes all changes and explanation in one transaction,
and increments the revision. Undo targets only the latest same-day accepted repair and
reverses it only when every affected value still matches the accepted result.

The retained focus-session ledger is updated with each checkpoint and supplies actual
active duration for Shutdown. Day settings, closure summary, and closure items are
durable. Closure and any final session checkpoint share one transaction;
`IKeepAwakeController.Release()` runs only after commit. Its prototype implementation is
an idempotent no-op and does not create a Windows-integration layer prematurely.

## Consequences

Every repair is reproducible from explicit rule order and can be explained without an
optimizer trace. Fixed work and shutdown are protected by both proposal construction and
transaction validation. Revision checks reject stale acceptance; value checks reject
stale undo. A closed date rejects task, session, settings, repair, and undo mutations.

SQLite migration 4 is additive apart from the `today_plans.schedule_revision` column and
backfills the single current checkpoint into the retained ledger when it still belongs
to the plan. No history screen, buffer editor, multi-day model, optimizer package,
background worker, or new project follows from the retained audit data.

## Supersedes

None. This decision extends [ADR 0002](0002-authoritative-time-and-recovery.md) and
[ADR 0004](0004-app-owned-sqlite-persistence.md).
