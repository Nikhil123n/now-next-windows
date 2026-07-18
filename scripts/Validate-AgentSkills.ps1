[CmdletBinding()]
param(
    [string] $SkillsRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SkillsRoot)) {
    $SkillsRoot = Join-Path $PSScriptRoot '..\.agents\skills'
}
$errors = [System.Collections.Generic.List[string]]::new()
$names = @{}
$executableExtensions = @(
    '.bat', '.cmd', '.com', '.dll', '.exe', '.jar', '.js', '.mjs', '.ps1',
    '.psd1', '.psm1', '.py', '.rb', '.sh', '.ts', '.vbs', '.wsf'
)

function Add-ValidationError {
    param([string] $Message)
    $errors.Add($Message)
}

function Read-Frontmatter {
    param(
        [string] $SkillFile,
        [string[]] $Lines
    )

    if ($Lines.Count -lt 3 -or $Lines[0] -ne '---') {
        Add-ValidationError "$SkillFile does not start with YAML frontmatter."
        return $null
    }

    $closing = -1
    for ($index = 1; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -eq '---') {
            $closing = $index
            break
        }
    }
    if ($closing -lt 2) {
        Add-ValidationError "$SkillFile has no closing YAML frontmatter delimiter."
        return $null
    }

    $metadata = @{}
    $index = 1
    while ($index -lt $closing) {
        $line = $Lines[$index]
        if ([string]::IsNullOrWhiteSpace($line)) {
            $index++
            continue
        }
        if ($line -notmatch '^([A-Za-z_][A-Za-z0-9_-]*):\s*(.*)$') {
            Add-ValidationError "$SkillFile has unsupported or invalid YAML at frontmatter line $($index + 1): $line"
            return $null
        }

        $key = $Matches[1]
        $value = $Matches[2]
        if ($metadata.ContainsKey($key)) {
            Add-ValidationError "$SkillFile repeats YAML key '$key'."
            return $null
        }

        if ($value -match '^[>|][-+]?$') {
            $parts = [System.Collections.Generic.List[string]]::new()
            $index++
            while ($index -lt $closing -and ($Lines[$index] -match '^\s+' -or [string]::IsNullOrWhiteSpace($Lines[$index]))) {
                $parts.Add($Lines[$index].Trim())
                $index++
            }
            $value = ($parts -join ' ').Trim()
        }
        else {
            $value = $value.Trim()
            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            $index++
        }
        $metadata[$key] = $value
    }

    return $metadata
}

$resolvedRoot = [System.IO.Path]::GetFullPath($SkillsRoot)
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Skills root does not exist: $resolvedRoot"
}

$skillDirectories = @(Get-ChildItem -LiteralPath $resolvedRoot -Directory | Sort-Object Name)
if ($skillDirectories.Count -eq 0) {
    Add-ValidationError 'No repository-scoped skills are installed.'
}
if ($skillDirectories.Count -gt 7) {
    Add-ValidationError "Installed skill count is $($skillDirectories.Count); maximum is 7."
}

foreach ($directory in $skillDirectories) {
    $skillFile = Join-Path $directory.FullName 'SKILL.md'
    if (-not (Test-Path -LiteralPath $skillFile -PathType Leaf)) {
        Add-ValidationError "$($directory.FullName) has no SKILL.md."
        continue
    }

    $lines = @(Get-Content -LiteralPath $skillFile -Encoding UTF8)
    $metadata = Read-Frontmatter -SkillFile $skillFile -Lines $lines
    if ($null -ne $metadata) {
        $name = [string] $metadata['name']
        $description = [string] $metadata['description']

        if ([string]::IsNullOrWhiteSpace($name) -or $name -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
            Add-ValidationError "$skillFile has a missing or invalid kebab-case name."
        }
        elseif ($names.ContainsKey($name)) {
            Add-ValidationError "Duplicate skill name '$name' in $skillFile and $($names[$name])."
        }
        else {
            $names[$name] = $skillFile
        }

        $triggerWords = '(?i)\b(use|when|choos\w*|writ\w*|run\w*|review\w*|debug\w*|apply\w*|recommend\w*|install\w*|creat\w*|fix\w*)\b'
        if ([string]::IsNullOrWhiteSpace($description) -or
            $description.Length -lt 40 -or
            $description -notmatch $triggerWords) {
            Add-ValidationError "$skillFile needs a clearer trigger description (at least 40 characters and an action/use cue)."
        }
    }

    $markdownFiles = @(Get-ChildItem -LiteralPath $directory.FullName -Recurse -File -Filter '*.md')
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
                Add-ValidationError "Broken relative reference '$target' in $($markdownFile.FullName)."
            }
        }
    }

    $skillFiles = @(Get-ChildItem -LiteralPath $directory.FullName -Recurse -File)
    foreach ($file in $skillFiles) {
        $extension = [System.IO.Path]::GetExtension($file.Name).ToLowerInvariant()
        if ($executableExtensions -contains $extension) {
            Add-ValidationError "Unexpected executable/script extension in skill: $($file.FullName)"
            continue
        }

        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        if ($bytes.Length -ge 2 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A) {
            Add-ValidationError "Unexpected Windows executable signature in skill: $($file.FullName)"
        }
        if ($bytes.Length -ge 2 -and $bytes[0] -eq 0x23 -and $bytes[1] -eq 0x21) {
            Add-ValidationError "Unexpected script shebang in skill: $($file.FullName)"
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "FAIL: skill validation found $($errors.Count) problem(s)." -ForegroundColor Red
    foreach ($validationError in $errors) {
        Write-Host " - $validationError" -ForegroundColor Red
    }
    exit 1
}

$fileCount = @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File).Count
Write-Host "PASS: validated $($skillDirectories.Count) skills and $fileCount files in $resolvedRoot"
Write-Host '      YAML frontmatter, unique names, trigger descriptions, relative references, and executable policy are valid.'
