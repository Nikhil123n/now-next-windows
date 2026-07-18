# 0003 — SQLite persistence and migrations

- Status: Accepted
- Date: 2026-07-18

## Context

The application needs reliable local recovery but has no distributed data problem.
Adding an ORM or generic repository would obscure the small set of product operations.

## Decision

Use `Microsoft.Data.Sqlite`, direct parameterized SQL, explicit transactions, and a
concrete store organized around product operations. Track ordered migrations in SQLite.
After a migration reaches `main`, changes use a new forward migration rather than editing
the existing file.

## Consequences

Recovery-critical writes complete before the interface reports success. Migration tests
cover clean creation and supported upgrades. SQL remains visible and reviewable. There is
no Entity Framework or generic repository abstraction.

## Supersedes

None.
