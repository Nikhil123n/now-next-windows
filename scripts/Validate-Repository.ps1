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
    'NowNext.slnx',
    'PRODUCT.md',
    'README.md',
    'SCOPE.md',
    'THIRD_PARTY_SKILLS.md',
    'docs\README.md',
    'docs\decisions\README.md',
    'docs\decisions\0001-windows-native-project-shape.md',
    'docs\decisions\0002-authoritative-time-and-recovery.md',
    'docs\decisions\0003-sqlite-persistence-and-migrations.md',
    'docs\decisions\0004-app-owned-sqlite-persistence.md',
    'docs\plans\PLAN_TEMPLATE.md',
    'docs\plans\prompt-2-application-scaffold.md',
    'docs\plans\prompt-3-today-domain-and-sqlite.md',
    'docs\plans\prompt-4-authoritative-timer-state-machine.md',
    'docs\plans\prompt-5-today-focus-vertical-slice.md',
    'docs\plans\prompt-6-context-break-journey.md',
    'docs\sqlite-schema.md',
    'docs\timer-invariants.md',
    'docs\testing\README.md',
    'docs\testing\prompt-5-manual-test-script.md',
    'docs\testing\prompt-6-manual-test-script.md',
    'scripts\Database-Dev.ps1',
    'scripts\Verify.ps1',
    'scripts\Validate-AgentSkills.ps1',
    'scripts\Validate-Repository.ps1',
    'src\NowNext.App\App.xaml',
    'src\NowNext.App\App.xaml.cs',
    'src\NowNext.App\Assets\Square150x150Logo.scale-200.png',
    'src\NowNext.App\Assets\Square44x44Logo.scale-200.png',
    'src\NowNext.App\Assets\StoreLogo.png',
    'src\NowNext.App\FocusSessionRuntime.cs',
    'src\NowNext.App\MainWindow.xaml',
    'src\NowNext.App\MainWindow.xaml.cs',
    'src\NowNext.App\NowNext.App.csproj',
    'src\NowNext.App\Package.appxmanifest',
    'src\NowNext.App\Persistence\Migrations\0001_initial_today_plan.sql',
    'src\NowNext.App\Persistence\Migrations\0002_current_focus_session_checkpoint.sql',
    'src\NowNext.App\Persistence\Migrations\0003_context_capsules_and_break_recovery.sql',
    'src\NowNext.App\Persistence\BreakSettings.cs',
    'src\NowNext.App\Persistence\TodayPlanStorageException.cs',
    'src\NowNext.App\Persistence\TodayPlanStore.Sessions.cs',
    'src\NowNext.App\Persistence\TodayPlanStore.Context.cs',
    'src\NowNext.App\Persistence\TodayPlanStore.cs',
    'src\NowNext.App\app.manifest',
    'src\NowNext.App\packages.lock.json',
    'src\NowNext.App\Presentation\FocusControlPolicy.cs',
    'src\NowNext.App\Presentation\TaskEditorInput.cs',
    'src\NowNext.App\Presentation\TimerDisplayFormatter.cs',
    'src\NowNext.App\Presentation\TodayTaskItem.cs',
    'src\NowNext.Core\NowNext.Core.csproj',
    'src\NowNext.Core\Domain\ScheduleEntry.cs',
    'src\NowNext.Core\Domain\ScheduleType.cs',
    'src\NowNext.Core\Domain\Task.cs',
    'src\NowNext.Core\Domain\TaskId.cs',
    'src\NowNext.Core\Domain\TaskImportance.cs',
    'src\NowNext.Core\Domain\TaskState.cs',
    'src\NowNext.Core\Domain\TimingMode.cs',
    'src\NowNext.Core\Domain\TodayPlan.cs',
    'src\NowNext.Core\Sessions\FocusSession.cs',
    'src\NowNext.Core\Sessions\BreakPrompt.cs',
    'src\NowNext.Core\Sessions\ContextCapsule.cs',
    'src\NowNext.Core\Sessions\FocusSessionMachine.cs',
    'src\NowNext.Core\Sessions\SessionCheckpoint.cs',
    'src\NowNext.Core\Sessions\SessionCommands.cs',
    'src\NowNext.Core\Sessions\SessionTypes.cs',
    'src\NowNext.Core\Sessions\SessionView.cs',
    'src\NowNext.Core\packages.lock.json',
    'tests\NowNext.Core.Tests\CoreAssemblySmokeTests.cs',
    'tests\NowNext.Core.Tests\Domain\TaskTests.cs',
    'tests\NowNext.Core.Tests\Domain\TodayPlanTests.cs',
    'tests\NowNext.Core.Tests\NowNext.Core.Tests.csproj',
    'tests\NowNext.Core.Tests\Persistence\MigrationTests.cs',
    'tests\NowNext.Core.Tests\Persistence\CurrentSessionStoreTests.cs',
    'tests\NowNext.Core.Tests\Persistence\ContextAndBreakStoreTests.cs',
    'tests\NowNext.Core.Tests\Persistence\TodayPlanStoreTests.cs',
    'tests\NowNext.Core.Tests\Presentation\FocusControlPolicyTests.cs',
    'tests\NowNext.Core.Tests\Presentation\BreakViewContractTests.cs',
    'tests\NowNext.Core.Tests\Presentation\FocusViewContractTests.cs',
    'tests\NowNext.Core.Tests\Presentation\TaskEditorInputTests.cs',
    'tests\NowNext.Core.Tests\Presentation\TimerDisplayFormatterTests.cs',
    'tests\NowNext.Core.Tests\Runtime\FocusSessionRuntimeTests.cs',
    'tests\NowNext.Core.Tests\Sessions\FocusSessionMachineTests.cs',
    'tests\NowNext.Core.Tests\Sessions\ContextCapsuleTests.cs',
    'tests\NowNext.Core.Tests\Sessions\SessionRecoveryTests.cs',
    'tests\NowNext.Core.Tests\Sessions\SessionTestClock.cs',
    'tests\NowNext.Core.Tests\Sessions\SessionTransitionMatrixTests.cs',
    'tests\NowNext.Core.Tests\TestSupport.cs',
    'tests\NowNext.Core.Tests\packages.lock.json'
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
    'docs\testing',
    'src\NowNext.App\Persistence\Migrations',
    'src\NowNext.App\Presentation',
    'src\NowNext.Core\Domain',
    'src\NowNext.Core\Sessions',
    'tests\NowNext.Core.Tests\Domain',
    'tests\NowNext.Core.Tests\Persistence',
    'tests\NowNext.Core.Tests\Presentation',
    'tests\NowNext.Core.Tests\Runtime',
    'tests\NowNext.Core.Tests\Sessions',
    'src\NowNext.App',
    'src\NowNext.Core',
    'tests\NowNext.Core.Tests'
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
        'validation:',
        'runs-on: windows-latest',
        'actions/checkout@v7',
        'actions/setup-dotnet@v6',
        'global-json-file: global.json',
        'cache-dependency-path: ''**/packages.lock.json''',
        'powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify.ps1'
    )
    foreach ($requirement in $workflowRequirements) {
        if (-not $workflow.Contains($requirement)) {
            Add-ValidationError "CI workflow is missing required content: $requirement"
        }
    }
    if ([regex]::Matches($workflow, 'runs-on: windows-latest').Count -ne 1) {
        Add-ValidationError 'CI workflow must contain exactly one Windows job.'
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

$expectedProjectPaths = @(
    'src\NowNext.App\NowNext.App.csproj',
    'src\NowNext.Core\NowNext.Core.csproj',
    'tests\NowNext.Core.Tests\NowNext.Core.Tests.csproj'
)
$actualProjectPaths = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Filter '*.csproj' |
    ForEach-Object { Get-RelativePathText $_.FullName } |
    Sort-Object)
$projectDifferences = @(Compare-Object -ReferenceObject $expectedProjectPaths -DifferenceObject $actualProjectPaths)
foreach ($difference in $projectDifferences) {
    $kind = if ($difference.SideIndicator -eq '<=') { 'Missing' } else { 'Unexpected' }
    Add-ValidationError "$kind project: $($difference.InputObject)"
}

$solutionFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -File |
    Where-Object { $_.Extension -in @('.sln', '.slnx') })
if ($solutionFiles.Count -ne 1 -or $solutionFiles[0].Name -ne 'NowNext.slnx') {
    Add-ValidationError 'The repository must contain only the NowNext.slnx solution.'
}
else {
    try {
        [xml] $solution = Get-Content -LiteralPath $solutionFiles[0].FullName -Encoding UTF8 -Raw
        $solutionProjectPaths = @($solution.SelectNodes('//Project') |
            ForEach-Object { $_.Path.Replace('/', '\') } |
            Sort-Object)
        $solutionDifferences = @(Compare-Object -ReferenceObject $expectedProjectPaths -DifferenceObject $solutionProjectPaths)
        foreach ($difference in $solutionDifferences) {
            Add-ValidationError "NowNext.slnx project mismatch: $($difference.InputObject)"
        }
    }
    catch {
        Add-ValidationError "NowNext.slnx is invalid XML: $($_.Exception.Message)"
    }
}

$repositoryFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File |
    Where-Object { $_.FullName -notlike "$repositoryRoot\.git\*" })
foreach ($file in $repositoryFiles) {
    if ($file.Name -match '^(?i:Dockerfile|docker-compose(?:\..+)?\.ya?ml|openapi\.ya?ml|azure-pipelines\.ya?ml|render\.ya?ml|vercel\.json)$') {
        Add-ValidationError "Prohibited cloud/server artifact: $(Get-RelativePathText $file.FullName)"
    }
}

$coreProjectPath = Join-Path $repositoryRoot 'src\NowNext.Core\NowNext.Core.csproj'
$appProjectPath = Join-Path $repositoryRoot 'src\NowNext.App\NowNext.App.csproj'
$testProjectPath = Join-Path $repositoryRoot 'tests\NowNext.Core.Tests\NowNext.Core.Tests.csproj'
if ((Test-Path -LiteralPath $coreProjectPath) -and
    (Test-Path -LiteralPath $appProjectPath) -and
    (Test-Path -LiteralPath $testProjectPath)) {
    [xml] $coreProject = Get-Content -LiteralPath $coreProjectPath -Encoding UTF8 -Raw
    [xml] $appProject = Get-Content -LiteralPath $appProjectPath -Encoding UTF8 -Raw
    [xml] $testProject = Get-Content -LiteralPath $testProjectPath -Encoding UTF8 -Raw

    if (@($coreProject.SelectNodes('//PackageReference')).Count -ne 0 -or
        @($coreProject.SelectNodes('//ProjectReference')).Count -ne 0) {
        Add-ValidationError 'NowNext.Core must remain dependency-free.'
    }

    $expectedAppPackages = @(
        'Microsoft.Data.Sqlite',
        'Microsoft.Windows.SDK.BuildTools',
        'Microsoft.Windows.SDK.BuildTools.WinApp',
        'Microsoft.WindowsAppSDK',
        'SQLitePCLRaw.bundle_e_sqlite3'
    )
    $actualAppPackages = @($appProject.SelectNodes('//PackageReference') |
        ForEach-Object { $_.GetAttribute('Include') } | Sort-Object)
    foreach ($difference in @(Compare-Object $expectedAppPackages $actualAppPackages)) {
        Add-ValidationError "NowNext.App package mismatch: $($difference.InputObject)"
    }

    $appReferences = @($appProject.SelectNodes('//ProjectReference') |
        ForEach-Object { $_.GetAttribute('Include') })
    if ($appReferences.Count -ne 1 -or
        $appReferences[0] -ne '..\NowNext.Core\NowNext.Core.csproj') {
        Add-ValidationError 'NowNext.App must reference only NowNext.Core.'
    }

    $embeddedMigrations = @($appProject.SelectNodes('//EmbeddedResource') |
        ForEach-Object { $_.GetAttribute('Include') })
    if ($embeddedMigrations.Count -ne 1 -or
        $embeddedMigrations[0] -ne 'Persistence\Migrations\*.sql') {
        Add-ValidationError 'NowNext.App must embed only its explicit SQL migration set.'
    }

    $testTargetFramework = [string] $testProject.Project.PropertyGroup.TargetFramework
    if ($testTargetFramework -ne 'net10.0-windows10.0.26100.0') {
        Add-ValidationError "NowNext.Core.Tests has unexpected TFM: $testTargetFramework"
    }

    $expectedTestReferences = @(
        '..\..\src\NowNext.App\NowNext.App.csproj',
        '..\..\src\NowNext.Core\NowNext.Core.csproj'
    )
    $actualTestReferences = @($testProject.SelectNodes('//ProjectReference') |
        ForEach-Object { $_.GetAttribute('Include') } | Sort-Object)
    foreach ($difference in @(Compare-Object $expectedTestReferences $actualTestReferences)) {
        Add-ValidationError "NowNext.Core.Tests project-reference mismatch: $($difference.InputObject)"
    }

    if (@($testProject.SelectNodes('//PackageReference')).Count -ne 0) {
        Add-ValidationError 'NowNext.Core.Tests must use the pinned MSTest SDK without direct packages.'
    }
}

$sqlitePackageOwners = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Filter '*.csproj' |
    Where-Object {
        (Get-Content -LiteralPath $_.FullName -Encoding UTF8 -Raw).Contains(
            '<PackageReference Include="Microsoft.Data.Sqlite"')
    } |
    ForEach-Object { Get-RelativePathText $_.FullName })
if ($sqlitePackageOwners.Count -ne 1 -or
    $sqlitePackageOwners[0] -ne 'src\NowNext.App\NowNext.App.csproj') {
    Add-ValidationError 'Microsoft.Data.Sqlite must be referenced only by NowNext.App.'
}

$expectedMigrations = @(
    '0001_initial_today_plan.sql',
    '0002_current_focus_session_checkpoint.sql',
    '0003_context_capsules_and_break_recovery.sql'
)
$migrationFiles = @(Get-ChildItem -LiteralPath (
        Join-Path $repositoryRoot 'src\NowNext.App\Persistence\Migrations') -File -Filter '*.sql' |
    ForEach-Object { $_.Name } |
    Sort-Object)
foreach ($difference in @(Compare-Object $expectedMigrations $migrationFiles)) {
    Add-ValidationError "Current migration mismatch: $($difference.InputObject)"
}

$documentationRequirements = @{
    'AGENTS.md' = 'current vertical slice'
    'ARCHITECTURE.md' = 'foreground `DispatcherQueueTimer`'
    'SCOPE.md' = '## Current vertical-slice boundary'
    'docs\testing\README.md' = 'Prompt 6 manual test script'
    'docs\sqlite-schema.md' = 'current_session_checkpoint'
    'docs\timer-invariants.md' = 'RecoveryRequired'
}
foreach ($requirement in $documentationRequirements.GetEnumerator()) {
    $path = Join-Path $repositoryRoot $requirement.Key
    if ((Test-Path -LiteralPath $path -PathType Leaf) -and
        -not (Get-Content -LiteralPath $path -Encoding UTF8 -Raw).Contains($requirement.Value)) {
        Add-ValidationError "$($requirement.Key) is missing current-phase documentation: $($requirement.Value)"
    }
}

$prohibitedDirectoryNames = @(
    'android', 'backend', 'cloud', 'ios', 'legacy', 'linux', 'macos', 'mobile',
    'old', 'server', 'services', 'web', 'website'
)
$repositoryDirectories = @(Get-ChildItem -LiteralPath $repositoryRoot -Recurse -Directory |
    Where-Object {
        $_.FullName -notlike "$repositoryRoot\.git*" -and
        $_.FullName -notlike "$repositoryRoot\.agents*"
    })
foreach ($directory in $repositoryDirectories) {
    if ($prohibitedDirectoryNames -contains $directory.Name.ToLowerInvariant()) {
        Add-ValidationError "The repository contains a prohibited implementation/legacy directory: $(Get-RelativePathText $directory.FullName)"
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
Write-Host '      Required files, authoritative hashes/references, links, policies, and current project boundaries are valid.'
Write-Host '      The solution contains exactly two production projects and one test project.'
