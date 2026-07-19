[CmdletBinding()]
param(
    [ValidateRange(5, 300)]
    [int] $IdleSeconds = 30,

    [ValidateRange(1, 1440)]
    [int] $LongRunMinutes = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resultsDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\qualification'))
$sampleIntervalSeconds = 30

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

function Find-NowNextWindow {
    param(
        [Parameter(Mandatory)]
        [TimeSpan] $Timeout
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'NOW/NEXT')
    while ($stopwatch.Elapsed -lt $Timeout) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $condition)
        if ($null -ne $window) {
            return $window
        }

        Start-Sleep -Milliseconds 100
    }

    throw "NOW/NEXT did not expose its main window within $($Timeout.TotalSeconds) seconds."
}

$packages = @(Get-AppxPackage -Name 'NowNext.LocalPrototype')
if ($packages.Count -ne 1) {
    throw "Install exactly one NowNext.LocalPrototype package before measuring; found $($packages.Count)."
}

$existing = @(Get-Process -Name 'NowNext.App' -ErrorAction SilentlyContinue)
if ($existing.Count -ne 0) {
    throw 'Close every NOW/NEXT instance before measuring cold start.'
}

$package = $packages[0]
$launchTarget = "shell:AppsFolder\$($package.PackageFamilyName)!App"
$coldStart = [System.Diagnostics.Stopwatch]::StartNew()
Start-Process -FilePath 'explorer.exe' -ArgumentList $launchTarget
$window = Find-NowNextWindow -Timeout (New-TimeSpan -Seconds 30)
$coldStart.Stop()
$processId = [int]$window.GetCurrentPropertyValue(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty)
$process = [System.Diagnostics.Process]::GetProcessById($processId)

try {
    Start-Sleep -Seconds 10
    $process.Refresh()
    $idleCpuStart = $process.TotalProcessorTime
    Start-Sleep -Seconds $IdleSeconds
    $process.Refresh()
    $idleCpu = $process.TotalProcessorTime - $idleCpuStart
    $logicalProcessors = [Environment]::ProcessorCount
    $idleNormalizedCpuPercent = 100 * $idleCpu.TotalSeconds /
        ($IdleSeconds * $logicalProcessors)
    $idleWorkingSetBytes = $process.WorkingSet64
    $idlePrivateBytes = $process.PrivateMemorySize64

    $longRunStartCpu = $process.TotalProcessorTime
    $longRunStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $longRunDuration = [TimeSpan]::FromMinutes($LongRunMinutes)
    $samples = [System.Collections.Generic.List[object]]::new()
    $maxWorkingSetBytes = $idleWorkingSetBytes
    $maxPrivateBytes = $idlePrivateBytes
    $unexpectedExitCode = $null
    while ($longRunStopwatch.Elapsed -lt $longRunDuration) {
        $remaining = $longRunDuration - $longRunStopwatch.Elapsed
        $sleepSeconds = [Math]::Min(
            $sampleIntervalSeconds,
            [Math]::Max(1, [Math]::Ceiling($remaining.TotalSeconds)))
        Start-Sleep -Seconds $sleepSeconds
        if ($process.HasExited) {
            $unexpectedExitCode = $process.ExitCode
            break
        }

        $process.Refresh()
        $maxWorkingSetBytes = [Math]::Max($maxWorkingSetBytes, $process.WorkingSet64)
        $maxPrivateBytes = [Math]::Max($maxPrivateBytes, $process.PrivateMemorySize64)
        $samples.Add([ordered]@{
                elapsed_seconds = [Math]::Round($longRunStopwatch.Elapsed.TotalSeconds, 1)
                responding = $process.Responding
                working_set_bytes = $process.WorkingSet64
                private_memory_bytes = $process.PrivateMemorySize64
                processor_seconds = [Math]::Round($process.TotalProcessorTime.TotalSeconds, 6)
            })
    }

    $process.Refresh()
    $longRunCpu = $process.TotalProcessorTime - $longRunStartCpu
    $computer = Get-CimInstance -ClassName Win32_ComputerSystem
    $operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem
    $batteries = @(Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue)
    $powerStatus = [System.Windows.Forms.SystemInformation]::PowerStatus
    $activePowerScheme = (& powercfg.exe /getactivescheme) -join ' '
    $primaryScreen = [System.Windows.Forms.Screen]::PrimaryScreen
    $result = [ordered]@{
        measured_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
        package_full_name = $package.PackageFullName
        device_manufacturer = $computer.Manufacturer
        device_model = $computer.Model
        windows_caption = $operatingSystem.Caption
        windows_version = $operatingSystem.Version
        primary_display_width = $primaryScreen.Bounds.Width
        primary_display_height = $primaryScreen.Bounds.Height
        logical_processors = $logicalProcessors
        power_line_status = $powerStatus.PowerLineStatus.ToString()
        battery_present = $batteries.Count -gt 0
        battery_charge_percent = if ($batteries.Count -gt 0) {
            $batteries[0].EstimatedChargeRemaining
        } else {
            $null
        }
        active_power_scheme = $activePowerScheme.Trim()
        cold_start_milliseconds = [Math]::Round($coldStart.Elapsed.TotalMilliseconds, 1)
        idle_observation_seconds = $IdleSeconds
        idle_processor_seconds = [Math]::Round($idleCpu.TotalSeconds, 6)
        idle_normalized_cpu_percent = [Math]::Round($idleNormalizedCpuPercent, 4)
        idle_working_set_bytes = $idleWorkingSetBytes
        idle_private_memory_bytes = $idlePrivateBytes
        long_run_observation_minutes = $LongRunMinutes
        long_run_observed_seconds = [Math]::Round(
            $longRunStopwatch.Elapsed.TotalSeconds,
            1)
        long_run_completed = $null -eq $unexpectedExitCode
        process_exit_code = $unexpectedExitCode
        long_run_processor_seconds = [Math]::Round($longRunCpu.TotalSeconds, 6)
        long_run_max_working_set_bytes = $maxWorkingSetBytes
        long_run_max_private_memory_bytes = $maxPrivateBytes
        long_run_all_samples_responding =
            $null -eq $unexpectedExitCode -and
            -not $samples.Where({ -not $_.responding })
        long_run_samples = $samples
    }

    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    $resultPath = Join-Path $resultsDirectory (
        'prototype-measurement-{0}.json' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 3
    Write-Host "Measurement record: $resultPath" -ForegroundColor Green
    if ($null -ne $unexpectedExitCode) {
        throw "NOW/NEXT exited during the long-run observation with code $unexpectedExitCode."
    }
}
finally {
    if (-not $process.HasExited) {
        $requestedClose = $process.CloseMainWindow()
        if ($requestedClose) {
            $process.WaitForExit(20000) | Out-Null
        }

        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }

    $process.Dispose()
}
