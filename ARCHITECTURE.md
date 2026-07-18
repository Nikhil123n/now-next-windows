# Architecture

This architecture serves the product defined in [FEATURES_FORWARD.md](FEATURES_FORWARD.md)
and respects every exclusion and evidence gate in
[FEATURES_DEFERRED_OR_REMOVED.md](FEATURES_DEFERRED_OR_REMOVED.md). Those files remain
authoritative when this overview is incomplete.

## Approved stack

- C# 14 on .NET 10 LTS SDK `10.0.302`.
- WinUI 3 on Windows App SDK `2.3.1`.
- SQLite through `Microsoft.Data.Sqlite` `10.0.10` and SQLitePCLRaw `3.0.3`.
- MSTest `4.3.2` on Microsoft.Testing.Platform.

Versions are centrally pinned. Adding or upgrading a dependency
requires a current feature need, license review, Windows/.NET compatibility review, and
passing verification. Do not add a package for logic that is clear in the platform or
standard library.

## Current production dependencies

`NowNext.App` is the only production project with packages. Its direct runtime packages
are `Microsoft.WindowsAppSDK` `2.3.1`, `Microsoft.Data.Sqlite` `10.0.10`, and
`SQLitePCLRaw.bundle_e_sqlite3` `3.0.3`. The direct SQLitePCLRaw bundle upgrades the
native SQLite line selected transitively by Microsoft.Data.Sqlite because version
`2.1.11` has a high-severity advisory. `Microsoft.Windows.SDK.BuildTools`
`10.0.28000.2270` and `Microsoft.Windows.SDK.BuildTools.WinApp` `0.4.0` are private
build/development tooling.

The App lock resolves these additional packages: `Microsoft.Data.Sqlite.Core`
`10.0.10`; `Microsoft.Web.WebView2` `1.0.3719.77`; `Microsoft.Windows.AI.MachineLearning`
`2.1.74`; `Microsoft.Windows.SDK.BuildTools.MSIX` `1.7.251221100`;
`Microsoft.WindowsAppSDK.AI` `2.3.4`; `Microsoft.WindowsAppSDK.Base` `2.0.4`;
`Microsoft.WindowsAppSDK.DWrite` `2.1.0`; `Microsoft.WindowsAppSDK.Foundation` `2.3.5`;
`Microsoft.WindowsAppSDK.InteractiveExperiences` `2.1.3`; `Microsoft.WindowsAppSDK.ML`
`2.1.74`; `Microsoft.WindowsAppSDK.Runtime` `2.3.1`; `Microsoft.WindowsAppSDK.Widgets`
`2.0.5`; `Microsoft.WindowsAppSDK.WinUI` `2.3.0`; `SourceGear.sqlite3` `3.50.4.5`;
`SQLitePCLRaw.config.e_sqlite3`, `SQLitePCLRaw.core`, and
`SQLitePCLRaw.provider.e_sqlite3` `3.0.3`; and `System.Numerics.Tensors` `9.0.0`.
These are resolved package contents, not permission to use deferred WebView, AI, widget,
or ML capabilities.

`NowNext.Core` remains package-free. `NowNext.Core.Tests` uses the globally pinned
MSTest SDK. `CommunityToolkit.Mvvm` `8.4.2` remains a centrally pinned future baseline
and is not referenced.

## Project boundaries

- `NowNext.Core` contains the immutable Today task and ordering model plus the pure,
  authoritative focus-session state machine. It owns domain validation, transitions,
  timer projections, and durable-checkpoint validation while remaining free of WinUI,
  Windows storage, and package dependencies.
- `NowNext.App` contains the plain Today and Focus WinUI surfaces, small stateless
  presentation formatters/policies, the narrow Windows lifecycle/runtime bridge, and the
  concrete SQLite store. It initializes the per-user database before loading Today and
  commits a transition before publishing it as successful.
- `NowNext.Core.Tests` targets Windows and references both production projects. Domain
  and persistence tests remain separated by namespace and directory.

There are at most two production projects and one test project. Use explicit constructor
composition or built-in .NET facilities. Do not introduce a generic repository, mediator,
AutoMapper, Entity Framework, WebView, external dependency-injection framework, or
future-platform adapter.

## State, timing, and recovery

Core owns the authoritative session state machine. UI events request transitions; views
do not calculate or persist authoritative elapsed time. Use `TimeProvider` so time can be
controlled in tests. Running states use `TimeProvider.GetTimestamp()` and
`GetElapsedTime()`; UTC wall time is only persistence and schedule metadata. A delayed UI
refresh changes presentation latency, not elapsed time. Persist committed active duration
and recovery checkpoints rather than trusting a continuously running UI timer.

Session state is immutable and transitions are explicit across Ready, Focusing, Paused,
LimitReached, Overtime, Landing, Break, Completed, Parked, RecoveryRequired, and
DayClosed. State-specific values replace unrelated booleans. Count-up and countdown are
projections over the same active elapsed value and approved limit; both use positive
overtime after that limit. Landing is an explicit five-minute active phase. Break elapsed
is separate from focus elapsed.

On restart, crash, sleep, resume, or long absence, present an explicit recovery choice.
Unobserved time is excluded unless the user deliberately includes it. Count-up and
countdown share transition rules and both enter positive overtime after their limit.
Persisted checkpoints never contain a reusable process-local monotonic timestamp. The App
serializes overlapping UI and power events through one built-in asynchronous gate. A
foreground `DispatcherQueueTimer` requests read-only Core projections for rendering and
periodic persisted checkpoints; it never increments elapsed time and stops outside the
Focus surface. There is no actor, channel, hosted service, or background worker.

The WinUI layer uses direct code-behind composition because there are only two screens
and no reusable application-service graph. Today mutations call the concrete store and
reload the immutable plan. Focus commands call `FocusSessionRuntime`; the view formats
only its `SessionView`. Introducing MVVM infrastructure or another layer requires a
demonstrated coordination or reuse need.

Schedule repair is a pure, deterministic proposal before persistence: preserve Fixed
items and shutdown, consume buffer, move Flexible work, then suggest a deferral if
needed. Applying the proposal is a separate, transactional, user-approved operation.

## Persistence and migrations

Use a concrete SQLite store organized around product operations, not a generic storage
interface. Execute parameterized SQL inside explicit transactions. Commit recovery-
critical data before reporting success to the UI.

The App stores `now-next.db` under the package user's LocalState directory. Schema version
1 supports create, edit, soft delete, reorder, and load for the injected clock's local
day. Prompt 4 adds schema version 2 for the single durable current-session checkpoint.
See [the SQLite schema contract](docs/sqlite-schema.md). Task rows are retained after
schedule deletion so current and future session records can keep stable references, but
no session-history table exists yet.

Maintain an ordered schema-migration table. Once a migration reaches `main`, never edit,
renumber, or reuse it; add a forward migration. Migration application is transactional
where SQLite permits it. Back up or fail clearly before any migration that cannot be made
safely reversible. Tests cover a fresh database and upgrades from every retained schema
baseline.

## Accessibility and diagnostics

Support keyboard and touch, semantic labels, focus order, high contrast, text scaling,
and Windows Reduced Motion. The blinking colon must not shift layout and becomes static
when reduced motion applies. Diagnostic logs are local and exclude task content unless
the user knowingly exports it.

The normal Focus visual state renders only the short focus label and segmented monospace
timer. Controls are a collapsed overlay revealed by intentional pointer, tap, or keyboard
input and hidden after inactivity. Recovery is intentionally not a normal focus state and
may keep its required decision controls visible.
