# `add`

Install one or more skills from a source into one or more agents.

## Synopsis

```
agentskills-cli add <source> [-g] [-a agent...] [-s skill...] [-y]
                         [--copy|--symlink]
                         [--nuget-source URL] [--npm-registry URL]
                         [--path PATH]
```

## Arguments

| Argument | Meaning |
|---|---|
| `<source>` | Local path, GitHub shorthand, URL, NuGet package id, or npm package id. See [Source formats](/sources/) for every shape accepted. |

## Flags

| Flag | Meaning |
|---|---|
| `-g`, `--global` | Install to the user-wide directory (`~/.agents/skills/...`) instead of the project. |
| `-a`, `--agent <NAME>` | Target a specific agent. Pass multiple times for multiple agents. Omit to auto-detect installed agents and (interactively) prompt. |
| `-s`, `--skill <NAME>` | Install specific skill(s) by name from the source. Use `'*'` for "all". Omit to install everything (with a multi-select prompt unless `-y`). |
| `-y`, `--yes` | Non-interactive. Skip all prompts and accept defaults: install all discovered skills, target all detected agents. Required for scripts/CI. |
| `--copy` | Materialize files into each agent's directory (default - cross-platform safe). |
| `--symlink` | Symlink each agent's directory to the canonical copy. Falls back to copy on filesystems that don't support it. |
| `--nuget-source <URL>` | One-shot override for the NuGet feed. By default `NuGet.Protocol` uses every enabled feed in your `NuGet.Config`. |
| `--npm-registry <URL>` | One-shot override for the default npm registry. By default `~/.npmrc` + project `.npmrc` + per-scope rules apply. |
| `--path <PATH>` | Restrict discovery to a subdirectory of the source. Use when a package puts its skills somewhere non-standard. Rejected if the path contains `..` or doesn't exist inside the staged source. |

## Examples

```bash
# Install one specific skill from a repo for Claude Code, non-interactive
agentskills-cli add vercel-labs/agent-skills -a claude-code -s web-design-guidelines -y

# Install everything in a NuGet package into the project, all detected agents
agentskills-cli add MyOrg.AgentSkills -y

# Install all skills from a private NuGet feed at a pinned version, globally
agentskills-cli add MyOrg.AgentSkills@1.2.3 -g -y \
  --nuget-source https://pkgs.contoso.com/v3/index.json

# Install from a GitHub branch with a subpath
agentskills-cli add https://github.com/vercel-labs/agent-skills/tree/main/skills/web-design-guidelines

# Install a local skill into the current project for Cursor
agentskills-cli add ./my-local-skill -a cursor

# Source uses a non-conventional layout - point at it explicitly
agentskills-cli add MyOrg.AgentSkills --path ai/prompts -y           # NuGet
agentskills-cli add @my-org/agent-skills --path src/skills -y         # npm
agentskills-cli add anthropics/skills --path docs/skills -y           # GitHub shorthand
agentskills-cli add ./my-local-skill --path subdir/skills -y          # local
```

## Output

After a successful install, the result table includes a `Path` column with the exact file location for every `(skill, agent)` pair, followed by an `Installed under …` summary listing the distinct directory roots:

```
╭─────────────┬─────────────┬────────┬─────────────────────────────────────────╮
│ Skill       │ Agent       │ Result │ Path                                    │
├─────────────┼─────────────┼────────┼─────────────────────────────────────────┤
│ hello-skill │ Universal   │ copied │ /path/to/project/.agents/skills/hello-… │
│ hello-skill │ Claude Code │ copied │ /path/to/project/.claude/skills/hello-… │
╰─────────────┴─────────────┴────────┴─────────────────────────────────────────╯
Installed under /path/to/project/.agents/skills, /path/to/project/.claude/skills
Done.
```

After the fact you can recover the same paths any time with `agentskills-cli list --paths` or by reading [Where files land](/reference/where-files-land).

## Discovery

Without `--path`, skills are found via three layered passes:

1. **Source-type conventions** - `contentFiles/any/any/skills/` for NuGet, `package/skills/` then `package/contentFiles/...` for npm, the priority directory list for git.
2. **Recursive scan** up to 5 levels deep, skipping `node_modules`, `.git`, `dist`, `build`, `__pycache__`.
3. With `--path`, the scan is restricted to that one subdirectory of the staged source.

## What gets written

- Skill files copied (or symlinked) to the install paths documented in [Where files land](/reference/where-files-land)
- Global lock entry in `~/.agents/.skill-lock.json` (always)
- Project lock entry in `./skills-lock.json` (only for project installs)

See [Lock files](/reference/lock-files) for the schema details.

## Result statuses

| Status in the Result column | Means |
|---|---|
| `copied` | Files materialized into the agent directory. |
| `symlinked` | Agent directory symlinked to the canonical `.agents/skills` copy. |
| `copy (symlink fell back)` | `--symlink` was requested but the filesystem refused; copied instead. |
| `canonical only` | Skill placed in the canonical `.agents/skills/`, but the agent-specific directory was deliberately skipped (project install, non-universal agent, agent's root config dir doesn't exist in the project). See [Where files land](/reference/where-files-land) for the rule. |
| `failed: <reason>` | Install couldn't complete; the row reports why. |
