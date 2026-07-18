# Third-Party Skills

## Installation summary

- Installation date: 2026-07-18
- Scope: repository-only under `.agents/skills/`
- Repository state before installation: empty, newly initialized Git repository with no commits
- Selected test framework: MSTest
- Installed skills: 4 (maximum allowed: 7)
- Third-party scripts executed during installation: none
- Official format/scope guidance reviewed: [OpenAI Codex — Build skills](https://developers.openai.com/codex/skills/)

The upstream repositories were cloned to an isolated temporary audit directory at the commits below. Every selected `SKILL.md`, applicable repository license, referenced local file, and executable file in each selected skill directory was inspected before installation. None of the selected skill directories contained executable scripts.

## Installed skills

### `run-tests`

- Upstream repository: <https://github.com/dotnet/skills>
- Exact source path: `plugins/dotnet-test/skills/run-tests`
- Pinned commit: `ab72985132b79adcc4818d1fc5c41d9543f12498`
- License: MIT, © .NET Foundation and Contributors
- Installed files: `SKILL.md`, `LICENSE`
- Local modifications: upstream instruction content is unchanged; line endings were normalized to LF, and the upstream root `LICENSE` was copied into the local skill directory
- Selection reason: current official .NET-team guidance for detecting VSTest versus Microsoft.Testing.Platform and using the .NET 10 test command syntax correctly

### `writing-mstest-tests`

- Upstream repository: <https://github.com/dotnet/skills>
- Exact source path: `plugins/dotnet-test/skills/writing-mstest-tests`
- Pinned commit: `ab72985132b79adcc4818d1fc5c41d9543f12498`
- License: MIT, © .NET Foundation and Contributors
- Installed files: `SKILL.md`, `LICENSE`
- Local modifications: upstream instruction content is unchanged; line endings were normalized to LF, and the upstream root `LICENSE` was copied into the local skill directory
- Selection reason: the narrow official authoring skill for MSTest, the test framework selected for this repository; it explicitly covers MSTest 3.x/4.x and is compatible with a .NET 10 test project

### `csharp-concurrency-patterns`

- Upstream repository: <https://github.com/Aaronontheweb/dotnet-skills>
- Exact source path: `skills/csharp-concurrency-patterns`
- Pinned commit: `c2ac7e9808f6636f21e99dd850363224459c5a3f`
- License: MIT, © 2025 Aaron Stannard
- Installed files: `SKILL.md`, `advanced-concurrency.md`, `LICENSE`
- Local modifications: upstream instruction/reference content is unchanged; line endings were normalized to LF, and the upstream root `LICENSE` was copied into the local skill directory
- Selection reason: directly relevant to a timer engine because it prioritizes `async`/`await`, cancellation, channels, and avoiding unsafe shared mutable state; its Akka.NET and Reactive Extensions material is optional guidance and installs no package or external service

### `andrej-karpathy-skill`

- Upstream repository: <https://github.com/duolahypercho/andrej-karpathy-skills>
- Exact source path: `skills/andrej-karpathy-skill`
- Pinned commit: `c2771d3ad0fd46f71d06fd9853cd6b0ad987737f`
- License: MIT, © 2026 Duola
- Installed files: `SKILL.md`, `LICENSE`
- Local modifications: upstream instruction content is unchanged; line endings were normalized to LF, and the upstream root `LICENSE` was copied into the local skill directory
- Selection reason: a single concise Codex-compatible variant covering think-before-coding, simplicity, surgical edits, and verifiable success criteria

## Rejections and substitutions

### No general .NET/C# skill installed from `dotnet/skills`

At the pinned commit, the core `plugins/dotnet` plugin contains only `skills/setup-local-sdk`. Its complete `SKILL.md` and the repository MIT license were inspected. It was rejected because it is an SDK installation workflow—not general C# development guidance—and centers on downloading/running installer scripts, preview or .NET 11 examples, cross-platform branches, and optional workload installation. Those concerns are unnecessary for a Windows-only .NET 10 WinUI 3 repository. Installing it as a substitute would create a misleading trigger and violate the small, directly relevant scope. No other current exact skill directory in `dotnet/skills` provides general modern C# coding guidance, so no substitute was installed.

The official test skills above remain useful without the full `dotnet` plugin. No LSP, plugin manifest, agent, marketplace, MCP server, or cloud tooling was copied.

### `debugging-checklist` rejected

- Upstream repository: <https://github.com/proflead/codex-skills-library>
- Exact source path inspected: `skills/foundation/debugging-checklist`
- Pinned commit inspected: `0279d8b0fff90554672c08dd4483e8f7d5ca5163`
- Rejection: the repository has no `LICENSE`, `COPYING`, or per-skill license grant. The complete `SKILL.md` was read and the directory contains no references or scripts, but copying unlicensed copyrighted material would fail the license acceptance requirement.
- Substitution: none. The Karpathy-inspired skill supplies a small amount of non-duplicative debugging discipline, but it is not represented as a replacement debugging checklist.

### `pr-reviewer` rejected

- Upstream repository: <https://github.com/proflead/codex-skills-library>
- Exact source path inspected: `skills/planning/pr-reviewer`
- Pinned commit inspected: `0279d8b0fff90554672c08dd4483e8f7d5ca5163`
- Rejection: the repository has no `LICENSE`, `COPYING`, or per-skill license grant. The complete `SKILL.md` was read and the directory contains no references or scripts, but copying it would fail the license acceptance requirement.
- Substitution: none; no broader review skill was added.

### Other exclusions

All ASP.NET, Blazor, MAUI, cloud, AI, data/EF Core, distributed-system, upgrade/migration, NuGet, MSBuild, diagnostics, template, and experimental skills in the inspected catalogs were excluded by scope. No alternate Karpathy variants were installed. MSTest was chosen because `writing-mstest-tests` is the current narrow test-authoring skill in the official .NET-team source; xUnit migration skills were not treated as authoring skills.

## Audit notes

- `run-tests`: only `SKILL.md`; no relative file references or executables.
- `writing-mstest-tests`: only `SKILL.md`; no relative file references or executables.
- `csharp-concurrency-patterns`: `SKILL.md` references `advanced-concurrency.md`; both were read completely; no executables.
- `andrej-karpathy-skill`: only `SKILL.md`; no relative file references or executables.
- `debugging-checklist` and `pr-reviewer`: each contains only `SKILL.md`; both were read completely; no executables.
- `setup-local-sdk`: contains only `SKILL.md`; it has inline shell and PowerShell examples but no executable files. No inline or repository script was executed.
- Codex's first-party `skill-installer` helper and its GitHub utility were inspected. The helper was attempted but failed before copying because this Windows workspace rejected child-process directory creation beneath `.agents/skills`; installation therefore used the already-audited pinned checkouts and workspace file editing. This first-party helper attempt did not execute upstream code.

## Validation

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-AgentSkills.ps1
```

The validator checks that there are at most seven immediate skill directories, each has parseable supported YAML frontmatter, names are unique and well formed, descriptions are clear enough for triggering, Markdown relative links resolve, and skill directories contain no executable/script files or executable file signatures.
