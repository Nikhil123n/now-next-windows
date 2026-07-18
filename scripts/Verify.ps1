[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    Invoke-ExternalCommand -FilePath 'powershell.exe' -Arguments @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        '.\scripts\Validate-Repository.ps1'
    )

    Invoke-ExternalCommand -FilePath 'dotnet' -Arguments @(
        'restore',
        '.\NowNext.slnx',
        '--locked-mode'
    )

    Invoke-ExternalCommand -FilePath 'dotnet' -Arguments @(
        'format',
        '.\NowNext.slnx',
        '--verify-no-changes',
        '--no-restore'
    )

    Invoke-ExternalCommand -FilePath 'dotnet' -Arguments @(
        'build',
        '.\NowNext.slnx',
        '--configuration',
        'Release',
        '--no-restore',
        '-warnaserror'
    )

    Invoke-ExternalCommand -FilePath 'dotnet' -Arguments @(
        'test',
        '--solution',
        '.\NowNext.slnx',
        '--configuration',
        'Release',
        '--no-build',
        '--results-directory',
        '.\TestResults',
        '--report-trx'
    )
}
finally {
    Pop-Location
}
