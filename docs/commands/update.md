# `update`

Check tracked skills for changes and reinstall the ones that changed. Works for every source type: GitHub, GitLab, generic git, NuGet, npm, well-known endpoints, and local folders.

To only look, use [`outdated`](/commands/outdated) (or `update --check`).

## Synopsis

```
agentskills-cli update [<target>...] [-g] [-p] [-a agent...] [-y] [--check] [--force]
                       [--prerelease] [--version-lock none|major|minor] [--json] [--exit-code]
                       [--nuget-source <URL>] [--npm-registry <URL>]
```

## Arguments

| Argument | Meaning |
|---|---|
| `<target>` | Optional. Each argument is matched as a skill name **or** a source, using the same parsing as [`add`](/commands/add): `owner/repo`, GitHub/GitLab/git URL, NuGet package id, npm package id, local path. Matching a package selects every skill installed from it. Omit to check everything tracked in the lock files. |
| `<package>@<version>` | For NuGet and npm, a version moves the package to **exactly** that version (upgrade or downgrade), e.g. `update Contoso.AgentSkills@2.1.0`. |

## Flags

| Flag | Meaning |
|---|---|
| `-g`, `--global` | Only the global scope. |
| `-p`, `--project` | Only the project scope. |
| Neither | Global, plus the project when the current directory has `skills-lock.json` or `.agents/skills/`. |
| `-a`, `--agent <NAME>` | Only skills installed for these agents, and reinstall only for them. |
| `-y`, `--yes` | Install without the confirmation prompt. |
| `--check` | Report only; don't install. Same as [`outdated`](/commands/outdated). |
| `--force` | Reinstall every matched skill even when no change is detected. Also the way to refresh entries that have no recorded baseline. |
| `--prerelease` | Consider prerelease NuGet/npm versions. Packages already on a prerelease always consider prereleases. |
| `--version-lock <LOCK>` | How far NuGet/npm packages may move. `none` (default): any newer version. `major`: stay on the installed major version (1.4.0 can go to 1.9.2, never 2.x). `minor`: stay on the installed major.minor (1.4.0 can go to 1.4.7, never 1.5). Applied per skill, relative to the version each one has installed. An explicit `Pkg@version` target ignores it. |
| `--json` | Write the result as JSON to stdout. Needs `--check` or `--yes` (it can't prompt). |
| `--exit-code` | With `--check`: exit `1` when updates are available, `2` when any skill could not be checked, `0` otherwise. |
| `--nuget-source <URL>` | Check and install NuGet packages from this feed only. |
| `--npm-registry <URL>` | Check and install npm packages from this registry. |

## How changes are detected

| Source | How |
|---|---|
| **NuGet** | Lists the package's versions on your configured feeds (your `NuGet.config` and credential providers) and compares with the locked version. Highest stable wins unless `--prerelease`; `--version-lock` narrows the range. |
| **npm** | Reads the packument from your configured registry (`.npmrc`, scoped registries, auth) and compares `dist-tags.latest` with the locked version. With `--version-lock` or `--prerelease`, the highest version in range wins instead. |
| **GitHub, global** | One [GitHub Trees API](#github-auth-lazy) call per repo; compares each skill folder's tree SHA with the one recorded at install. Falls back to cloning if the API is unavailable. |
| **GitHub (project), GitLab, generic git** | Shallow-clones the recorded repo and ref, and compares the skill folder with the recorded baseline (tree SHA for global installs, content hash for project installs). |
| **Well-known endpoints, local folders** | Fetches/reads the source again and compares the skill folder's content hash. |

Each source is fetched at most once per run, and the copy fetched during the check is reused for the install.

## What a reinstall preserves

- **Scope**: global skills stay global, project skills stay in the project. Each scope's lock file is updated, never the other.
- **Agents**: the skill is reinstalled for the agents that currently have it (narrow with `-a`).
- **Mode**: symlinked installs stay symlinked, copied installs stay copied.
- **Name**: a skill installed under another name (`add --as`) keeps that name.
- **Selection**: only the skills you installed from a package are updated. If a new package version drops a skill, that skill is reported as failed rather than silently removed.

## Statuses

| Status | Meaning |
|---|---|
| `up to date` | No change detected. |
| `update available` | A newer version or changed content was found (check mode). |
| `updated` | Reinstalled. |
| `skipped` | Can't be checked automatically. The reason column says why, for example a skill installed from a local `.nupkg` file, or an entry with no recorded baseline (use `--force`). |
| `failed` | The check or the reinstall failed: feed or network error, skill removed upstream, and so on. |

**Stale lock entries.** A lock entry whose skill isn't installed in that scope is reported as skipped. `update` (without `--check`) removes it. Versions before 1.1.0 wrote project installs into the global lock too, so expect a few of these the first time you run `update -g`.

## GitHub auth (lazy)

The token resolution chain for the Trees API:

1. Try unauthenticated (60 requests/hour per IP; one request per repo).
2. On a rate-limit 403, try `GITHUB_TOKEN`.
3. Then `GH_TOKEN`.
4. Then `gh auth token`.

Clones use your normal git credentials.

## Examples

```bash
# What would change? (same as `agentskills-cli outdated`)
agentskills-cli update --check

# Update everything, no prompt
agentskills-cli update -y

# Update one package (every skill installed from it)
agentskills-cli update Contoso.AgentSkills -y
agentskills-cli update @acme/agent-skills -y
agentskills-cli update anthropics/skills -g -y

# Take bug-fix and feature releases, but no breaking (major) versions
agentskills-cli update --version-lock major -y

# Move a package to an exact version (upgrade or downgrade)
agentskills-cli update Contoso.AgentSkills@2.1.0 -y

# Only one skill, only for Claude Code
agentskills-cli update web-design-guidelines -a claude-code -y

# Re-pull a skill even though nothing changed
agentskills-cli update web-design-guidelines --force -y

# CI: fail the job when skills are out of date
agentskills-cli update --check --exit-code
```

## Output

```
                                 Update check
╭───────────────────────┬─────────┬──────────────────────────┬─────────┬─────────┬──────────────────╮
│ Skill                 │ Scope   │ Source                   │ Current │ Latest  │ Status           │
├───────────────────────┼─────────┼──────────────────────────┼─────────┼─────────┼──────────────────┤
│ sample-greeter        │ project │ Contoso.Skills@1.0.0 (…) │ 1.0.0   │ 1.2.0   │ update available │
│ web-design-guidelines │ global  │ vercel-labs/… (github)   │ 0a1b2c3 │ 3116f3e │ update available │
╰───────────────────────┴─────────┴──────────────────────────┴─────────┴─────────┴──────────────────╯
4 up to date, 2 update(s) available
```

With `--json`:

```json
{
  "items": [
    {
      "name": "sample-greeter",
      "scope": "project",
      "source": "Contoso.Skills@1.0.0",
      "sourceType": "nuget",
      "status": "updateAvailable",
      "current": "1.0.0",
      "latest": "1.2.0",
      "agents": ["claude-code", "universal"]
    }
  ],
  "summary": { "upToDate": 4, "updateAvailable": 1, "updated": 0, "skipped": 0, "failed": 0 }
}
```
