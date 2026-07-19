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
and is not referenced. Prompts 8 and 9 add no production dependency.

## Project boundaries

- `NowNext.Core` contains the immutable Today task and ordering model, the pure
  authoritative focus-session state machine, the deterministic same-day repair engine,
  and workday projections. It owns domain validation, transitions, timer/recovery/
  Shutdown projections, and durable-checkpoint validation while remaining free of
  WinUI, Windows storage, and package dependencies.
- `NowNext.App` contains the plain Today and Focus WinUI surfaces, small stateless
  presentation formatters/policies, narrow Windows path/settings/power/accessibility
  services, the lifecycle/runtime bridge, privacy-safe local diagnostics, and the
  concrete SQLite store and data-maintenance operations. It initializes the per-user
  database before loading Today and commits a transition before publishing success.
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
LimitReached, Overtime, Landing, Break, BreakCompleted, Completed, Parked, Abandoned,
RecoveryRequired, and DayClosed. State-specific values replace unrelated booleans.
Count-up and countdown are
projections over the same active elapsed value and approved limit; both use positive
overtime after that limit. Landing is an explicit five-minute active phase. Break elapsed
is separate from focus elapsed, stops at its approved limit, and then waits for an
explicit return command. Parking requires a next action; explicit Abandon is the distinct
terminal route when the user will not resume the task.

On restart, crash, sleep, resume, or long absence, present an explicit recovery choice.
Unobserved time is excluded unless the user deliberately includes it. Count-up and
countdown share transition rules and both enter positive overtime after their limit.
Persisted checkpoints never contain a reusable process-local monotonic timestamp. The App
serializes overlapping UI and power events through one built-in asynchronous gate. A
foreground `DispatcherQueueTimer` requests read-only Core projections for rendering and
periodic persisted checkpoints; it never increments elapsed time and stops outside the
Focus surface. There is no actor, channel, hosted service, or background worker.

The foreground UI observes its refresh interval with a monotonic timestamp. A missed
interval of at least 15 minutes reloads the last durable checkpoint as
RecoveryRequired, excluding the unobserved tail. Short render delays continue to read
the normal authoritative monotonic projection. Recovery shows one decision column with
the next Fixed commitment and realistic nonnegative time before the earlier of that
commitment or shutdown; rebuilding only creates a proposal.

The WinUI layer uses direct code-behind composition because there is one small vertical
slice and no reusable application-service graph. Today mutations call the concrete store
and reload the immutable plan. Focus commands call `FocusSessionRuntime`; the view
formats only its `SessionView`. Introducing MVVM infrastructure or another layer
requires a demonstrated coordination or reuse need.

Schedule repair is a pure, deterministic proposal before persistence. Local offsets from
midnight prevent wraparound: exclude resolved work, protect the authoritative current
finish, Fixed intervals, and shutdown, place Flexible work in stable order through
available gaps, then suggest the latest Normal Flexible task (or latest Important task)
if one deferral is needed. One insufficient deferral or a protected-time conflict yields
a non-acceptable impossible proposal. Applying a feasible changed proposal is a separate
user-approved transaction guarded by the exact schedule revision and shutdown. Undo
reverses only the latest same-day accepted repair and refuses if an affected value has
since changed.

## Persistence and migrations

Use a concrete SQLite store organized around product operations, not a generic storage
interface. Execute parameterized SQL inside explicit transactions. Commit recovery-
critical data before reporting success to the UI.

The App stores `now-next.db` under the package user's LocalState directory. Schema version
1 supports create, edit, soft delete, reorder, and load for the injected clock's local
day. Version 2 adds the single durable current-session checkpoint. Version 3 extends that
checkpoint for bounded Break recovery and adds minimal Context Capsules and Break
settings. Version 4 adds schedule revisions, explicit day settings, a retained
focus-session ledger, accepted-repair explanation/undo records, and durable day closure.
The current checkpoint is backfilled into the ledger when it still belongs to the plan.
Park, task lifecycle, checkpoint, ledger, repair, and closure changes use explicit
transactions. See [the SQLite schema contract](docs/sqlite-schema.md). Task, capsule, and
ledger rows are retained after schedule deletion, but there is no general history view.

Shutdown requires an explicitly configured time. Its summary totals retained active
focus (including Landing and excluding Break), persists closure and any final session
checkpoint before publishing success, and then calls the narrow
`IKeepAwakeController.Release()` hook. The production hook is an idempotent Windows
display request; isolated callers may use the no-op. A release failure cannot reopen the
durable day.

Backup and export use SQLite's online backup operation rather than copying an open file.
Every produced or selected database must pass `quick_check`, foreign-key integrity, and
the exact known migration sequence. Restore stages the source, retains a validated live
rollback image until post-replacement validation succeeds, and then reloads the runtime
through Recovery Mode. Complete reset builds a fresh version-4 database and removes only
the exact package LocalState backup, export, diagnostic, and app-setting data after
confirmation. Prompt 8 adds no migration.

Maintain an ordered schema-migration table. Once a migration reaches `main`, never edit,
renumber, or reuse it; add a forward migration. Migration application is transactional
where SQLite permits it. Back up or fail clearly before any migration that cannot be made
safely reversible. Tests cover a fresh database and upgrades from every retained schema
baseline.

## Windows integration and local data safety

`WindowsApplicationDataPaths` is the single package-identity-aware source for the live
database, backup, export, and diagnostic locations. `WindowsUserSettings` owns only the
keep-display-awake and full-screen-startup booleans. A disabled-by-default packaged
startup task implements launch at sign-in and preserves Windows states controlled by the
user in Task Manager or by policy.

The App wraps `PowerManager.SystemSuspendStatusChanged` in a narrow event source. One
`WindowsLifecycleCoordinator` serializes callbacks, commits the existing runtime's
interruption checkpoint before observed suspend/close, releases keep-awake, and reloads
the durable checkpoint on resume. Reload never invokes a resume command: active Focus,
Landing, and Break checkpoints therefore appear as `RecoveryRequired` with unobserved
time excluded. Unexpected power loss or forced process termination still relies on the
existing periodic durable checkpoint and cannot be made transactional by desktop
lifecycle notification.

The user-controlled keep-awake setting uses only
`Windows.System.Display.DisplayRequest`. It is acquired for actively accruing Focus,
overtime, Landing, or Break and released for Ready, pause, boundaries, terminal states,
Recovery, suspension, day close, and exit. It does not request system execution and does
not override explicit sleep, lid-close, power, or shutdown actions. Optional full-screen
startup changes only the initial `AppWindow` presenter; Focus/Break behavior is unchanged.

Interfaces exist only for Windows calls that deterministic tests replace: application
paths/settings, display keep-awake, startup task, Reduced Motion, and power events. They
are Windows integration seams, not cross-platform adapters or a generalized service
layer.

## Prototype qualification and packaging

Release-candidate qualification remains repository tooling. The build command first runs
the canonical verification path, publishes the existing x64 packaged App, creates or
reuses a non-exportable CurrentUser code-signing key with subject
`CN=NowNext Development`, exports only its public certificate beside the MSIX, and
records SHA-256 hashes. The elevated installer verifies the package signer against that
certificate and imports only the public certificate into
`LocalMachine\\TrustedPeople`; it never changes a Trusted Root store. This is narrow
one-device development signing and does not implement the deferred production-signing,
Store, updater, channel, or enterprise-deployment capabilities.

Performance qualification records observed cold-start duration, normalized idle CPU,
working/private memory, responsiveness, and long-run samples without introducing an
application telemetry path or invented pass threshold. Results live under ignored local
artifacts. Forced-termination qualification targets only the installed package process
and verifies recovery through its already durable checkpoint.

## Accessibility and diagnostics

Support keyboard and touch, semantic labels, focus order, high contrast, text scaling,
and Windows Reduced Motion. The blinking colon must not shift layout and becomes static
when reduced motion applies. The local JSON-lines diagnostic log accepts controlled
event/result identifiers, UTC timestamps, and exception type names only. Its API has no
arbitrary message field, so task titles, notes, Context Capsule content, selected paths,
and exception messages are excluded by construction. Exported databases knowingly
contain user data; diagnostics never do.

The normal Focus visual state renders only the short focus label and segmented monospace
timer. Controls are a collapsed overlay revealed by intentional pointer, tap, or keyboard
input and hidden after inactivity. Recovery is intentionally not a normal focus state and
may keep its required decision controls visible.
