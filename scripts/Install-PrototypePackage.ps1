[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [string] $CertificatePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installer from an elevated PowerShell window (Run as administrator).'
}

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path

if ([System.IO.Path]::GetExtension($resolvedPackage) -ne '.msix') {
    throw "PackagePath must identify the generated .msix file."
}

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    $certificateCandidates = @(Get-ChildItem `
        -LiteralPath (Split-Path -Parent $resolvedPackage) `
        -File `
        -Filter '*.cer')
    if ($certificateCandidates.Count -ne 1) {
        throw 'CertificatePath was omitted and exactly one .cer was not found beside the package.'
    }

    $CertificatePath = $certificateCandidates[0].FullName
}

$resolvedCertificate = (Resolve-Path -LiteralPath $CertificatePath).Path
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $resolvedCertificate)
$signature = Get-AuthenticodeSignature -LiteralPath $resolvedPackage
if ($null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw 'The package signer does not match the supplied prototype certificate.'
}

$existingPackages = @(Get-AppxPackage -Name 'NowNext.LocalPrototype')
if ($existingPackages.Count -ne 0) {
    throw 'NOW/NEXT is already registered. Back up LocalState and remove the existing package before a clean install.'
}

Import-Certificate `
    -FilePath $resolvedCertificate `
    -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

Add-AppxPackage `
    -Path $resolvedPackage `
    -ForceApplicationShutdown

$installed = @(Get-AppxPackage -Name 'NowNext.LocalPrototype')
if ($installed.Count -ne 1) {
    throw "Expected one installed NowNext.LocalPrototype package; found $($installed.Count)."
}

Write-Host "Installed $($installed[0].PackageFullName)." -ForegroundColor Green
Write-Host "Local data: $env:LOCALAPPDATA\Packages\$($installed[0].PackageFamilyName)\LocalState"
Write-Host "Trusted certificate thumbprint: $($certificate.Thumbprint)"
