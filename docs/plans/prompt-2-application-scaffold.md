# Plan: Scaffold the packaged WinUI application

- Owner: Prompt 2 implementation
- Status: Complete
- Updated: 2026-07-18
- Related decision/issue: [Windows-native project shape](../decisions/0001-windows-native-project-shape.md)

## Goal and observable success

Create the approved three-project .NET 10 solution. The packaged WinUI application
launches to a plain readiness message, Core remains free of product behavior, and the
MSTest project proves the Core assembly can be loaded.

## Constraints and assumptions

- Preserve the product invariants and exclusions in the
  [forward feature register](../../FEATURES_FORWARD.md) and
  [deferred/removed register](../../FEATURES_DEFERRED_OR_REMOVED.md).
- Add no navigation, task behavior, timing, persistence, themes, services, or future
  abstractions.
- Use only the pinned .NET, Windows App SDK, build-tooling, and MSTest dependencies.

## Steps

1. [x] Create the solution, packaged application shell, empty Core library, and smoke test.
2. [x] Replace the Prompt 1 CI branch with one canonical local and CI verification path.
3. [x] Update architecture and repository documentation to describe the Prompt 2 state.

## Verification

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1
```

Launch the packaged application with `dotnet run`, confirm the readiness message is
visible, and close the launched instance.

Completed on 2026-07-18:

- The canonical command passed repository validation, locked restore, formatting
  verification, Release build, and the Core assembly smoke test. The build reported
  zero warnings and zero errors; the test run passed 1 of 1 tests.
- Packaged `dotnet run` registered and launched the app. Windows UI Automation found
  `NOW/NEXT prototype is ready.` as an enabled text control in the responsive
  `NOW/NEXT` window, after which the window closed normally.
- The package manifest uses `Windows.FullTrustApplication`, the required desktop
  activation entry point. Declaring the managed `App` class as the package entry point
  incorrectly activated it as a WinRT application class before the generated WinUI
  startup path.

Environment used for verification:

- Windows 11 Home `10.0.26200` x64 with Developer Mode enabled, Windows SDK
  `10.0.26100.0`, and Windows App Runtime `2.3.1.0` x64 reporting `Ok`.
- Temporary .NET SDK `10.0.302`, .NET runtime `10.0.10`, MSBuild
  `18.6.11.33009`, and `dotnet format`
  `10.0.302-servicing.26329.109+35b593bebfcba58f8e78298cef14c2761f5d86c6`.
- Windows PowerShell `5.1.26100.8875`, Windows SDK Build Tools package
  `10.0.28000.2270`, WinApp CLI `0.4.0`, and MSTest SDK `4.3.2`.
- Visual Studio Build Tools 2026 `18.5.1` is installed. Visual Studio
  WinUI/MSIX designer and debugger integration was not exercised and is not required
  by the verified command-line build and packaged-launch path.

## Risks and rollback

The primary risks are WinUI/MSIX toolchain availability and drift between local and CI
commands. Package versions and lock files make restore repeatable, while the shared
verification script prevents a separate CI-only path. There is no persisted user data;
rollback consists of removing the Prompt 2 scaffold and restoring the Prompt 1 harness.
