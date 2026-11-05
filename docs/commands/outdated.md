# `outdated`

Report which tracked skills have updates available. Installs nothing. Alias: `check`.

`outdated` is the read-only half of [`update`](/commands/update): it runs the same checks for every source type (NuGet and npm versions, git tree SHAs, content hashes) and prints the same table.

## Synopsis

```
agentskills-cli outdated [<target>...] [-g] [-p] [-a agent...] [--prerelease]
                         [--version-lock none|major|minor]
                         [--json] [--exit-code]
                         [--nuget-source <URL>] [--npm-registry <URL>]
```

Arguments and flags mean the same as for [`update`](/commands/update#flags).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Check finished (always, unless `--exit-code` is passed). |
| `1` | With `--exit-code`: at least one update is available. A target that matched no tracked skill also exits `1`. |
| `2` | With `--exit-code`: at least one skill could not be checked (feed down, repo moved, ...). |

## Examples

```bash
# Everything tracked (global, plus this project if it has skills)
agentskills-cli outdated

# One package
agentskills-cli outdated Contoso.AgentSkills
agentskills-cli outdated @acme/agent-skills

# Include prereleases
agentskills-cli outdated --prerelease

# Only report updates that stay on each package's current major version
agentskills-cli outdated --version-lock major

# CI gate
agentskills-cli outdated -p --exit-code

# Machine-readable
agentskills-cli outdated --json | jq '.items[] | select(.status == "updateAvailable") | .name'
```
