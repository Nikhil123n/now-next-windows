[CmdletBinding()]
param(
    [switch] $Inspect,
    [switch] $Reset,
    [switch] $ConfirmReset
)

$ErrorActionPreference = 'Stop'
$prototypePackageName = 'NowNext.LocalPrototype'
$prototypeProcessName = 'NowNext.App'
$databaseFileName = 'now-next.db'

if ($Inspect -eq $Reset) {
    throw 'Specify exactly one operation: -Inspect or -Reset.'
}

if ($ConfirmReset -and -not $Reset) {
    throw '-ConfirmReset is valid only with -Reset.'
}

$packages = @(
    Get-AppxPackage -Name $prototypePackageName |
        Where-Object { $_.Name -eq $prototypePackageName }
)
if ($packages.Count -ne 1) {
    throw "Expected exactly one installed $prototypePackageName package; found $($packages.Count)."
}

$package = $packages[0]
$localStateDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState"))
$databasePath = [System.IO.Path]::GetFullPath(
    (Join-Path $localStateDirectory $databaseFileName))
$candidatePaths = @(
    $databasePath,
    "$databasePath-wal",
    "$databasePath-shm",
    "$databasePath-journal"
)

$localStatePrefix = $localStateDirectory.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($candidatePath in $candidatePaths) {
    $resolvedCandidate = [System.IO.Path]::GetFullPath($candidatePath)
    if (-not $resolvedCandidate.StartsWith(
            $localStatePrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing database operation outside the prototype LocalState directory: $resolvedCandidate"
    }

    if ([System.IO.Path]::GetDirectoryName($resolvedCandidate) -ne $localStateDirectory) {
        throw "Refusing nested database target: $resolvedCandidate"
    }
}

if ($Inspect) {
    Write-Host "Prototype package: $($package.PackageFullName)"
    Write-Host "LocalState: $localStateDirectory"
    Write-Host "Database: $databasePath"

    $existingFiles = @($candidatePaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($existingFiles.Count -eq 0) {
        Write-Host 'Database files: none'
        exit 0
    }

    Get-Item -LiteralPath $existingFiles |
        Select-Object FullName, Length, LastWriteTimeUtc
    exit 0
}

if (-not $ConfirmReset) {
    throw 'Reset requires the explicit -ConfirmReset switch.'
}

$runningProcesses = @(Get-Process -Name $prototypeProcessName -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    throw "Refusing reset while $prototypeProcessName is running."
}

$removedFiles = 0
foreach ($candidatePath in $candidatePaths) {
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
        Remove-Item -LiteralPath $candidatePath -Force
        $removedFiles++
    }
}

Write-Host "Reset complete. Removed $removedFiles prototype database file(s) from $localStateDirectory"
