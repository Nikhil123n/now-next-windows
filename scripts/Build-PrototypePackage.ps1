[CmdletBinding()]
param(
    [switch] $SkipVerification
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$packageRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot 'NowNext-Prototype-1.0.0.0-x64'))
$intermediateRoot = [System.IO.Path]::GetFullPath((Join-Path $packageRoot '_build'))
$certificateSubject = 'CN=NowNext Development'

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

function Assert-SafeGeneratedPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $RequiredParent
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedParent = [System.IO.Path]::GetFullPath($RequiredParent)
    $parentPrefix = $resolvedParent.TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith(
            $parentPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated path '$resolvedPath' must remain beneath '$resolvedParent'."
    }
}

Assert-SafeGeneratedPath -Path $packageRoot -RequiredParent $artifactsRoot
Assert-SafeGeneratedPath -Path $intermediateRoot -RequiredParent $packageRoot

Push-Location $repositoryRoot
try {
    if (-not $SkipVerification) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1
        if ($LASTEXITCODE -ne 0) {
            throw "Canonical verification failed with exit code $LASTEXITCODE."
        }
    }

    if (Test-Path -LiteralPath $packageRoot) {
        Remove-Item -LiteralPath $packageRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $intermediateRoot -Force | Out-Null

    $certificate = @(Get-ChildItem -Path Cert:\CurrentUser\My |
        Where-Object {
            $_.Subject -eq $certificateSubject -and
            $_.HasPrivateKey -and
            $_.NotAfter -gt [DateTime]::Now.AddDays(30) -and
            $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3'
        } |
        Sort-Object -Property NotAfter -Descending |
        Select-Object -First 1)

    if ($certificate.Count -eq 0) {
        $certificate = @(New-SelfSignedCertificate `
            -Type Custom `
            -Subject $certificateSubject `
            -FriendlyName 'NOW/NEXT local prototype package signing' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyUsage DigitalSignature `
            -KeyExportPolicy NonExportable `
            -NotAfter ([DateTimeOffset]::Now.AddYears(3).DateTime) `
            -TextExtension @(
                '2.5.29.19={text}',
                '2.5.29.37={text}1.3.6.1.5.5.7.3.3'
            ))
    }

    $certificatePath = Join-Path $packageRoot 'NowNext-Prototype.cer'
    Export-Certificate `
        -Cert $certificate[0] `
        -FilePath $certificatePath `
        -Type CERT | Out-Null

    Invoke-ExternalCommand -FilePath 'dotnet' -Arguments @(
        'publish',
        '.\src\NowNext.App\NowNext.App.csproj',
        '--configuration',
        'Release',
        '--runtime',
        'win-x64',
        '--no-restore',
        '-p:GenerateAppxPackageOnBuild=true',
        '-p:AppxPackageSigningEnabled=true',
        "-p:PackageCertificateThumbprint=$($certificate[0].Thumbprint)",
        "-p:AppxPackageDir=$intermediateRoot/",
        '-p:AppxBundle=Never',
        '-p:UapAppxPackageBuildMode=SideloadOnly',
        '-p:GenerateAppInstallerFile=false',
        '-p:AppxSymbolPackageEnabled=false',
        '-p:DebugSymbols=false',
        '-p:DebugType=None'
    )

    $packages = @(Get-ChildItem -LiteralPath $intermediateRoot -Recurse -File -Filter '*.msix' |
        Where-Object { $_.FullName -notlike '*\Dependencies\*' })
    if ($packages.Count -ne 1) {
        throw "Expected exactly one generated MSIX package; found $($packages.Count)."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $packages[0].FullName
    if ($null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $certificate[0].Thumbprint) {
        throw 'The generated package was not signed by the exported prototype certificate.'
    }

    $finalPackagePath = Join-Path $packageRoot 'NowNext.App_1.0.0.0_x64.msix'
    Copy-Item -LiteralPath $packages[0].FullName -Destination $finalPackagePath
    $finalPackage = Get-Item -LiteralPath $finalPackagePath
    Remove-Item -LiteralPath $intermediateRoot -Recurse -Force

    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'scripts\Install-PrototypePackage.ps1') `
        -Destination (Join-Path $packageRoot 'Install-PrototypePackage.ps1')
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'docs\release-candidate\README.md') `
        -Destination (Join-Path $packageRoot 'README.md')

    $hashLines = @($finalPackage, (Get-Item -LiteralPath $certificatePath)) | ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        "$($hash.Hash)  $($_.Name)"
    }
    Set-Content `
        -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') `
        -Value $hashLines `
        -Encoding UTF8

    Write-Host "Package: $($finalPackage.FullName)" -ForegroundColor Green
    Write-Host "Certificate: $certificatePath"
    Write-Host 'Install from an elevated PowerShell so Windows can trust the local certificate.'
}
finally {
    Pop-Location
}
