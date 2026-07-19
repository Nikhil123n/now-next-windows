# NOW/NEXT local prototype release candidate

This package is an owner-only, offline-capable Windows 11 prototype. It is not a Store
release and has no automatic updater. It is signed by a repository-generated local
development certificate for explicit installation on the owner's Surface only.

## Prerequisites

- Windows 11 x64 build `10.0.22000` or later.
- Windows Developer Mode enabled for prototype sideloading.
- Windows App Runtime `2.3.1` x64 installed for the framework-dependent WinUI package.
- For rebuilding only: .NET SDK `10.0.302`, the pinned NuGet packages, Windows SDK
  `10.0.26100`, and PowerShell 5.1 or later. Building requires package restore access;
  the installed application does not require a network connection.

## Build and install

From the repository root, create the verified package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-PrototypePackage.ps1
```

The command runs canonical verification, builds the x64 MSIX under
`artifacts\NowNext-Prototype-1.0.0.0-x64`, creates or reuses a non-exportable
`CN=NowNext Development` code-signing key in the current user's certificate store,
exports only its public certificate, and writes SHA-256 hashes for both artifacts.

Open PowerShell with **Run as administrator**, then install from inside that generated
directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-PrototypePackage.ps1 `
  -PackagePath .\NowNext.App_1.0.0.0_x64.msix `
  -CertificatePath .\NowNext-Prototype.cer
```

This release-candidate installer deliberately requires a clean identity. Before running
it, `Get-AppxPackage -Name NowNext.LocalPrototype` must return nothing. If a prior CLI
development registration exists, first make a validated external backup, then remove the
old registration with the uninstall command below. Package removal can delete LocalState.

The installer refuses a non-elevated shell, verifies that the package signer matches the
supplied public certificate, trusts that certificate only in
`LocalMachine\\TrustedPeople`, and installs the fixed `NowNext.LocalPrototype` identity.
It never modifies Trusted Root Certification Authorities. Launch **NOW/NEXT** from Start.

## Local data, backup, and recovery

The database and all application-owned files are local to this Windows user:

```text
%LOCALAPPDATA%\Packages\NowNext.LocalPrototype_fwksbhw01wbay\LocalState\
  now-next.db
  Backups\
  Exports\
  Diagnostics\now-next.log.jsonl
```

Use **Backup** before replacing or uninstalling the package. Backup uses SQLite online
backup and validates the copy. **Export** creates a validated copy at a user-selected
location outside package LocalState; that is the safest form to retain across uninstall.
**Restore** validates integrity, foreign keys, and the exact known migration sequence
before replacement, retains a rollback image until the replacement validates, and routes
an active checkpoint through Recovery Mode. **Reset data** is confirmation-gated and
removes only the exact package LocalState data owned by NOW/NEXT.

After a crash, forced termination, Windows restart, sleep, or a substantial absence, the
app restores only committed active duration and requires an explicit Recovery choice.
Suspended time is excluded unless the owner deliberately includes it.

## Qualification commands

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Measure-Prototype.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Restart-PrototypeForRecoveryTest.ps1
```

The measurement command records raw cold-start, idle CPU, idle memory, responsiveness,
and long-run observations under `artifacts\qualification`; it does not impose invented
performance thresholds. For the recovery command, first start an active session and let
a durable checkpoint occur. The command verifies that it targets the installed package,
forcibly terminates it, relaunches it, and leaves the owner to confirm the visible
Recovery state and committed time.

## Uninstall

Export or copy a validated backup outside LocalState first. Removing an MSIX can remove
its per-user LocalState, so uninstall must be treated as destructive to local app data.

```powershell
Get-AppxPackage -Name NowNext.LocalPrototype | Remove-AppxPackage
```

Uninstall does not delete an exported database stored elsewhere. Reinstalling the same
identity is not a substitute for a backup. The local public certificate remains trusted
after uninstall so a later rebuild can be installed. To remove it too, use an elevated
PowerShell and target the exact exported certificate thumbprint:

```powershell
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
  (Resolve-Path .\NowNext-Prototype.cer).Path)
Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
```

## Known limitations

- The package is x64, framework-dependent, locally self-signed, and requires Developer
  Mode plus one explicit elevated certificate-trust/install step. This is development
  signing, not a production trust chain, and is unsuitable for external distribution.
- There is no Store publishing, automatic update, remote crash reporting, telemetry,
  cloud backup, or multi-device recovery.
- Unexpected power loss can preserve only the most recently committed checkpoint; it
  cannot make uncommitted in-memory time durable. Recovery never invents the missing tail.
- WinUI touch, keyboard, text scaling, High Contrast, Reduced Motion, Surface lid,
  Modern Standby, display wake, battery, and long active-session behavior require the
  recorded manual Surface qualification in
  `docs/testing/prompt-9-release-candidate-test.md`.
- Visual Studio's XAML/MSIX designers and debugger integration remain unqualified. The
  documented .NET CLI workflow is the verified development path.
