# `remove`

Remove installed skills.

## Synopsis

```
agentskills remove [<target>...] [-g] [-a agent...] [-y]
```

## Arguments

| Argument | Meaning |
|---|---|
| `<target>` | Skills to remove. Each argument is matched first as a skill name, then as a source - any shape `add` accepts works (GitHub `owner/repo` or URL, GitLab URL, NuGet package id, npm package id, local path). Repeatable; the match is a union. Omit to be prompted with a multi-select of everything installed. |

## Flags

| Flag | Meaning |
|---|---|
| `-g`, `--global` | Remove from the global scope. Default removes from both scopes. |
| `-a`, `--agent <NAME>` | Limit removal to specific agents. By default all agents that have the skill get cleaned. |
| `-y`, `--yes` | Skip the "Remove N skill(s)?" confirmation. |

## Examples

```bash
# Remove by skill name
agentskills remove hello-skill -y

# Remove every skill installed from a package - same parsing as `add`
agentskills remove nuget:MyOrg.AgentSkills -y          # NuGet (bare 'MyOrg.AgentSkills' also works)
agentskills remove @acme/sample-skills -y              # npm
agentskills remove anthropics/skills -y                # GitHub
agentskills remove https://gitlab.com/group/repo -y    # GitLab

# Mix skill names and sources, narrow to one agent
agentskills remove some-extra-skill MyOrg.AgentSkills -a claude-code -y
```

## Versions

Same behavior as in [`list`](/commands/list#versions). A bare package id (`MyOrg.AgentSkills`) matches every installed version - useful for upgrades where you don't remember which version is live. Pinning a version (`MyOrg.AgentSkills@1.2.3`) strict-matches, and if nothing matches the command prints a hint with the installed version(s).

Scoped npm names are handled correctly: in `@acme/sample-skills@1.0.0` the leading `@` is the scope marker, only the trailing `@1.0.0` is the version.

## What gets removed

After removal:

- Skill files deleted from every (matching) agent directory
- Canonical `.agents/skills/<name>` directory cleaned up
- Lock entries dropped (global lock always; project lock if the skill was project-scoped)

## Non-interactive safety

In a non-interactive shell (CI, piped stdin), running `agentskills remove` with **no targets** refuses with exit code 2 rather than removing everything. You must pass explicit targets in scripts.

```
$ agentskills remove -y
Refusing to remove all skills without explicit targets. Pass them as arguments.
```

This protects against a stray script call accidentally wiping a user's global skill set.
