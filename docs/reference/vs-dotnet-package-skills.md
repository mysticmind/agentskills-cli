# AgentSkills CLI vs dotnet-package-skills

[`dotnet-package-skills`](https://github.com/NuGet/Client.Tools/tree/main/dotnet-package-skills) is a tool from the NuGet team. It copies the skills bundled in the NuGet packages your project already references into `.agents/skills/`. It is narrower than AgentSkills CLI and does that one job carefully. This page compares the two as of November 2026, when `dotnet-package-skills` was still a prerelease.

## Same package layout

Both tools read skills from `skills/<name>/SKILL.md` at the root of a NuGet package, so **one package works with both**. Package authors should follow [Publishing to NuGet](/authoring/publishing-nuget): one folder per skill, prefixed with the package id, with the folder name identical to the skill's `name`.

## How they work

| | AgentSkills CLI | dotnet-package-skills |
|---|---|---|
| Where skills come from | NuGet feeds, npm registries, GitHub, GitLab, any git URL, well-known endpoints, local folders and package files | NuGet packages a project already references, read from the local NuGet packages folder after `dotnet restore` |
| How you pick them | You name a package or source: `add Contoso.Widgets` | It reads the project: `install` with no arguments finds the solution and its direct package references |
| Network | Downloads what you add | None; works offline once the project is restored |
| Where skills go | Every detected agent's folder (symlinked or copied), project or user-wide | One destination folder per run, project only |
| Tracking | `skills-lock.json` (project) and `~/.agents/.skill-lock.json` (global), shared with `vercel-labs/skills` | `.dotnet-package-skills.json` inside the destination folder |

## Where dotnet-package-skills is stronger today

- **It follows the project automatically.** Re-run it after upgrading a package and the skills move to the version the project references, with no version to type.
- **Stale skills.** It reports skills whose package left the project, and `uninstall --stale` removes them.
- **`--dry-run`** on install and uninstall.
- **Several versions of one package**, with explicit rules for each case.
- **A polished interactive picker** with descriptions, paging, and resize handling.

## Where AgentSkills CLI goes further

- **Sources beyond the project's NuGet packages**: npm, git hosts, well-known endpoints, local folders, and NuGet packages your project doesn't reference.
- **Every agent in one run**, with symlinks, in project or user-wide (global) scope.
- **Updates on your terms**: [`outdated`](/commands/outdated) and [`update`](/commands/update) check feeds and repos, with `--version-lock`, exact `Pkg@version` targets, and `--exit-code` for CI.
- **Team and CI workflows**: [`restore`](/commands/restore) rebuilds a checkout from `skills-lock.json`, `--frozen` fails on drift, and `--json` output on most commands.
- **Name conflicts you can resolve**: install under another name with `--as`, or `--replace`; `dotnet-package-skills` skips a colliding skill and reports an error.
- **Shared state with `vercel-labs/skills`**: the lock files are the same format.

## Using both

They coexist safely: each tool refuses to overwrite a skill folder it didn't install. AgentSkills CLI reports a [name conflict](/commands/add#name-conflicts) for a folder that `dotnet-package-skills` manages, and `dotnet-package-skills` skips a folder that AgentSkills CLI installed, with a warning. Let one tool own each skill, though: if both manage the same skill folder, their records drift apart.

## Which to use

- **Only skills from NuGet packages your project references, kept in step with the project**: `dotnet-package-skills` is purpose-built for this.
- **Anything else**: skills from git or npm, several agents, global installs, update checks, CI restores, or one tool for a polyglot team: AgentSkills CLI.
