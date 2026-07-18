[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError {
    param([string] $Message)
    $errors.Add($Message)
}

function Get-RelativePathText {
    param([string] $Path)
    $rootUri = [Uri]::new(($repositoryRoot.TrimEnd('\') + '\'))
    $pathUri = [Uri]::new([System.IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Test-FileHash {
    param(
        [string] $RelativePath,
        [string] $ExpectedHash
    )
    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-ValidationError "Required fingerprinted file is missing: $RelativePath"
        return
    }
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedHash) {
        Add-ValidationError "$RelativePath changed unexpectedly. Expected SHA-256 $ExpectedHash; found $actualHash."
    }
}

Write-Host 'Running audited repository-skill validation...'
$skillValidator = Join-Path $PSScriptRoot 'Validate-AgentSkills.ps1'
if (-not (Test-Path -LiteralPath $skillValidator -PathType Leaf)) {
    Add-ValidationError 'Missing scripts\Validate-AgentSkills.ps1.'
}
else {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $skillValidator
    if ($LASTEXITCODE -ne 0) {
        Add-ValidationError "Validate-AgentSkills.ps1 failed with exit code $LASTEXITCODE."
    }
}

$requiredFiles = @(
    '.editorconfig',
    '.gitattributes',
    '.gitignore',
    '.github\dependabot.yml',
    '.github\ISSUE_TEMPLATE\bug.yml',
    '.github\ISSUE_TEMPLATE\config.yml',
    '.github\ISSUE_TEMPLATE\feature.yml',
    '.github\pull_request_template.md',
    '.github\workflows\ci.yml',
    'AGENTS.md',
    'ARCHITECTURE.md',
    'CONTRIBUTING.md',
    'Directory.Build.props',
    'Directory.Packages.props',
    'FEATURES_DEFERRED_OR_REMOVED.md',
    'FEATURES_FORWARD.md',
    'global.json',
    'LICENSE',
    'PRODUCT.md',
    'README.md',
    'SCOPE.md',
    'THIRD_PARTY_SKILLS.md',
    'docs\README.md',
    'docs\decisions\README.md',
    'docs\decisions\0001-windows-native-project-shape.md',
    'docs\decisions\0002-authoritative-time-and-recovery.md',
    'docs\decisions\0003-sqlite-persistence-and-migrations.md',
    'docs\plans\PLAN_TEMPLATE.md',
    'docs\testing\README.md',
    'scripts\Validate-AgentSkills.ps1',
    'scripts\Validate-Repository.ps1'
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf)) {
        Add-ValidationError "Required repository file is missing: $relativePath"
    }
}

$requiredDirectories = @(
    '.agents\skills',
    '.github\ISSUE_TEMPLATE',
    '.github\workflows',
    'docs\decisions',
    'docs\plans',
    'docs\testing'
)
foreach ($relativePath in $requiredDirectories) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Container)) {
        Add-ValidationError "Required repository directory is missing: $relativePath"
    }
}

Test-FileHash -RelativePath 'FEATURES_FORWARD.md' -ExpectedHash 'B78EB6B37217A39444A5CBEEC69B4EFF2D27BA08A32687EF31B98E43AE6ABF3E'
Test-FileHash -RelativePath 'FEATURES_DEFERRED_OR_REMOVED.md' -ExpectedHash '0B3CBB0D58E5293CE794EB451AFAAF1658D52C81660EB02B54B79A2CDBB636B5'
Test-FileHash -RelativePath 'THIRD_PARTY_SKILLS.md' -ExpectedHash '8AAABD13FAB3FBE66CFBAA2CB25ED927301C2161655D5EB4C60E1DFDFEEE0DBF'
Test-FileHash -RelativePath 'scripts\Validate-AgentSkills.ps1' -ExpectedHash '9FDD0D73EBBAA14A0A082D911E41C94E2646CB59D9EC208737950CEB8E12B1C2'

$skillDirectories = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.agents\skills') -Directory -ErrorAction SilentlyContinue)
if ($skillDirectories.Count -ne 4) {
    Add-ValidationError "Expected exactly 4 audited skills; found $($skillDirectories.Count)."
}

$agentsPath = Join-Path $repositoryRoot 'AGENTS.md'
if (Test-Path -LiteralPath $agentsPath -PathType Leaf) {
    $agentLineCount = @(Get-Content -LiteralPath $agentsPath -Encoding UTF8).Count
    if ($agentLineCount -gt 100) {
        Add-ValidationError "AGENTS.md has $agentLineCount lines; maximum is 100."
    }
}

$scopeMapFiles = @('AGENTS.md', 'PRODUCT.md', 'SCOPE.md', 'ARCHITECTURE.md', 'docs\README.md')
foreach ($relativePath in $scopeMapFiles) {
    $path = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }
    $content = Get-Content -LiteralPath $path -Encoding UTF8 -Raw
    foreach ($register in @('FEATURES_FORWARD.md', 'FEATURES_DEFERRED_OR_REMOVED.md')) {
        if ($content -notmatch [regex]::Escape($register)) {
            Add-ValidationError "$relativePath does not reference $register."
        }
    }
}

$markdownFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Filter '*.md' |
    Where-Object { $_.FullName -notlike "$repositoryRoot\.git\*" })
foreach ($markdownFile in $markdownFiles) {
    $content = Get-Content -LiteralPath $markdownFile.FullName -Encoding UTF8 -Raw
    $linkMatches = [regex]::Matches($content, '\[[^\]]+\]\((?<target>[^)]+)\)')
    foreach ($linkMatch in $linkMatches) {
        $target = $linkMatch.Groups['target'].Value.Trim().Trim('<', '>')
        if ($target -match '^(?i:https?://|mailto:|data:|#)') {
            continue
        }
        $target = ($target -split '#', 2)[0]
        $target = ($target -split '\?', 2)[0]
        if ([string]::IsNullOrWhiteSpace($target)) {
            continue
        }
        $target = [Uri]::UnescapeDataString($target)
        $resolvedTarget = [System.IO.Path]::GetFullPath((Join-Path $markdownFile.DirectoryName $target))
        if (-not (Test-Path -LiteralPath $resolvedTarget)) {
            Add-ValidationError "Broken relative Markdown link '$target' in $(Get-RelativePathText $markdownFile.FullName)."
        }
    }
}

try {
    Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Encoding UTF8 -Raw |
        ConvertFrom-Json | Out-Null
}
catch {
    Add-ValidationError "global.json is invalid JSON: $($_.Exception.Message)"
}

foreach ($propsFile in @('Directory.Build.props', 'Directory.Packages.props')) {
    try {
        [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot $propsFile) -Encoding UTF8 -Raw) | Out-Null
    }
    catch {
        Add-ValidationError "$propsFile is invalid XML: $($_.Exception.Message)"
    }
}

$yamlFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.github') -Recurse -File |
    Where-Object { $_.Extension -in @('.yml', '.yaml') })
foreach ($yamlFile in $yamlFiles) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $yamlFile.FullName -Encoding UTF8) {
        $lineNumber++
        if ($line.Contains("`t")) {
            Add-ValidationError "YAML contains a tab in $(Get-RelativePathText $yamlFile.FullName) at line $lineNumber."
        }
        if ($line -match '^( +)\S' -and ($Matches[1].Length % 2) -ne 0) {
            Add-ValidationError "YAML indentation is not a multiple of two in $(Get-RelativePathText $yamlFile.FullName) at line $lineNumber."
        }
    }
}

$workflowPath = Join-Path $repositoryRoot '.github\workflows\ci.yml'
if (Test-Path -LiteralPath $workflowPath -PathType Leaf) {
    $workflow = Get-Content -LiteralPath $workflowPath -Encoding UTF8 -Raw
    $workflowRequirements = @(
        'push:',
        'pull_request:',
        'repository-validation:',
        'application-validation:',
        'runs-on: windows-latest',
        'actions/checkout@v6',
        '.\scripts\Validate-Repository.ps1',
        "if: steps.application.outputs.exists != 'true'",
        "if: steps.application.outputs.exists == 'true'",
        'actions/setup-dotnet@v5',
        'dotnet restore .\NowNext.slnx --locked-mode',
        'dotnet format .\NowNext.slnx --verify-no-changes --no-restore',
        'dotnet build .\NowNext.slnx --configuration Release --no-restore -warnaserror',
        'dotnet test --solution .\NowNext.slnx --configuration Release --no-build --results-directory .\TestResults --report-trx'
    )
    foreach ($requirement in $workflowRequirements) {
        if (-not $workflow.Contains($requirement)) {
            Add-ValidationError "CI workflow is missing required content: $requirement"
        }
    }
    if ([regex]::Matches($workflow, 'runs-on: windows-latest').Count -ne 2) {
        Add-ValidationError 'CI workflow must contain exactly two Windows jobs.'
    }
}

$dependabotPath = Join-Path $repositoryRoot '.github\dependabot.yml'
if (Test-Path -LiteralPath $dependabotPath -PathType Leaf) {
    $dependabot = Get-Content -LiteralPath $dependabotPath -Encoding UTF8 -Raw
    foreach ($requirement in @('version: 2', 'package-ecosystem: nuget', 'package-ecosystem: github-actions', 'interval: weekly')) {
        if (-not $dependabot.Contains($requirement)) {
            Add-ValidationError "Dependabot policy is missing required content: $requirement"
        }
    }
}

$implementationExtensions = @(
    '.appxmanifest', '.cs', '.csproj', '.db', '.msixmanifest', '.sln', '.slnx',
    '.sqlite', '.sqlite3', '.xaml'
)
$repositoryFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File |
    Where-Object { $_.FullName -notlike "$repositoryRoot\.git\*" })
foreach ($file in $repositoryFiles) {
    if ($implementationExtensions -contains $file.Extension.ToLowerInvariant()) {
        Add-ValidationError "Prompt 1 must not contain product implementation: $(Get-RelativePathText $file.FullName)"
    }
    if ($file.Name -match '^(?i:Dockerfile|docker-compose(?:\..+)?\.ya?ml|openapi\.ya?ml|azure-pipelines\.ya?ml|render\.ya?ml|vercel\.json)$') {
        Add-ValidationError "Prohibited cloud/server artifact: $(Get-RelativePathText $file.FullName)"
    }
}

$prohibitedDirectoryNames = @(
    'android', 'backend', 'cloud', 'ios', 'legacy', 'linux', 'macos', 'mobile',
    'old', 'server', 'services', 'src', 'test', 'tests', 'web', 'website'
)
$repositoryDirectories = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -Directory |
    Where-Object {
        $_.FullName -notlike "$repositoryRoot\.git*" -and
        $_.FullName -notlike "$repositoryRoot\.agents*"
    })
foreach ($directory in $repositoryDirectories) {
    if ($prohibitedDirectoryNames -contains $directory.Name.ToLowerInvariant()) {
        Add-ValidationError "Prompt 1 contains a prohibited implementation/legacy directory: $(Get-RelativePathText $directory.FullName)"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "FAIL: repository validation found $($errors.Count) problem(s)." -ForegroundColor Red
    foreach ($validationError in $errors) {
        Write-Host " - $validationError" -ForegroundColor Red
    }
    exit 1
}

Write-Host "PASS: repository specification validated at $repositoryRoot" -ForegroundColor Green
Write-Host '      Required files, authoritative hashes/references, links, policies, and Prompt 1 boundaries are valid.'
Write-Host '      Application validation is not yet applicable; Prompt 2 will scaffold the real solution.'
