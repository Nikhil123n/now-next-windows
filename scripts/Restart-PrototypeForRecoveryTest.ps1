[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient

$packages = @(Get-AppxPackage -Name 'NowNext.LocalPrototype')
if ($packages.Count -ne 1) {
    throw "Install exactly one NowNext.LocalPrototype package; found $($packages.Count)."
}

$package = $packages[0]
$processes = @(Get-Process -Name 'NowNext.App' -ErrorAction SilentlyContinue)
if ($processes.Count -ne 1) {
    throw "Start exactly one packaged NOW/NEXT instance; found $($processes.Count)."
}

$process = $processes[0]
$processPath = [System.IO.Path]::GetFullPath($process.Path)
$installRoot = [System.IO.Path]::GetFullPath($package.InstallLocation)
$installPrefix = $installRoot.TrimEnd('\') + '\'
if (-not $processPath.StartsWith(
        $installPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The running process is not beneath the installed package path '$installRoot'."
}

$localState = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState"))
$databasePath = Join-Path $localState 'now-next.db'
$before = if (Test-Path -LiteralPath $databasePath) {
    Get-Item -LiteralPath $databasePath
}
else {
    $null
}

Stop-Process -Id $process.Id -Force
$process.WaitForExit(10000) | Out-Null
Start-Process `
    -FilePath 'explorer.exe' `
    -ArgumentList "shell:AppsFolder\$($package.PackageFamilyName)!App"

$condition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::NameProperty,
    'NOW/NEXT')
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$window = $null
while ($stopwatch.Elapsed -lt [TimeSpan]::FromSeconds(30)) {
    $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
    if ($null -ne $window) {
        break
    }

    Start-Sleep -Milliseconds 100
}

if ($null -eq $window) {
    throw 'NOW/NEXT did not relaunch within 30 seconds.'
}

$after = if (Test-Path -LiteralPath $databasePath) {
    Get-Item -LiteralPath $databasePath
}
else {
    $null
}
if ($null -ne $before -and $null -eq $after) {
    throw 'The LocalState database disappeared after forced termination.'
}

Write-Host 'Forced termination and packaged relaunch completed.' -ForegroundColor Green
Write-Host "Database: $databasePath"
Write-Host 'Confirm the visible app is in Recovery Mode and shows only committed time.'
