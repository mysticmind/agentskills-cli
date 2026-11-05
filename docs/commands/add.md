# `add`

Install one or more skills from a source into one or more agents.

## Synopsis

```
agentskills-cli add <source> [-g] [-a agent...] [-s skill...] [-y]
                         [--copy|--symlink]
                         [--nuget-source URL] [--npm-registry URL]
                         [--path PATH] [--as [SKILL=]NAME...] [--replace]
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
| `--copy` | Materialize files into each agent's directory instead of symlinking. |
| `--symlink` | Symlink each agent's directory to the canonical copy (the default). Falls back to copy on filesystems that don't support it. |
| `--nuget-source <URL>` | One-shot override for the NuGet feed. By default `NuGet.Protocol` uses every enabled feed in your `NuGet.Config`. |
| `--npm-registry <URL>` | One-shot override for the default npm registry. By default `~/.npmrc` + project `.npmrc` + per-scope rules apply. |
| `--path <PATH>` | Restrict discovery to a subdirectory of the source. Use when a package puts its skills somewhere non-standard. Rejected if the path contains `..` or doesn't exist inside the staged source. |
| `--as <SKILL>=<NAME>` | Install a skill under a different name, typically to [resolve a name conflict](#name-conflicts). `--as <NAME>` alone works when one skill is being installed. Repeatable. Names use lowercase letters, digits and hyphens. |
| `--replace` | Replace existing skills from other sources that have the same name, without asking. |

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

# Install a local skill directory into the current project for Cursor
agentskills-cli add ./my-local-skill -a cursor

# Install from a local .nupkg or .tgz file (no feed / registry lookup)
agentskills-cli add ./Contoso.SampleSkills.1.4.0.nupkg -y
agentskills-cli add ./contoso-sample-skills-2.1.0.tgz -y

# Source uses a non-conventional layout - point at it explicitly
agentskills-cli add MyOrg.AgentSkills --path ai/prompts -y           # NuGet
agentskills-cli add @my-org/agent-skills --path src/skills -y         # npm
agentskills-cli add anthropics/skills --path docs/skills -y           # GitHub shorthand
agentskills-cli add ./my-local-skill --path subdir/skills -y          # local
```

## Name conflicts

Skills are installed into a folder named after the skill, so two sources that both ship a skill called `greeter` would land in the same place. `add` checks this before installing anything. A **conflict** is a skill with the same name that already exists in the target scope and came from a different source, or that isn't tracked in the lock file at all (installed by hand or by another tool). These are *not* conflicts:

- Re-adding from the same source, including a different version or ref. That's an update.
- The same name in the other scope (global vs project).

What happens on a conflict:

- **Interactive**: for each conflicting skill you're asked to install it under a different name (a name is suggested, for example `greeter-contoso` for a skill from `Contoso.AgentSkills`), replace the existing skill, or skip it.
- **`-y` or non-interactive (CI)**: nothing is installed. The conflicts are listed with the exact commands that resolve them, and the exit code is `2`.

```
$ agentskills-cli add Contoso.AgentSkills -y
Skill name conflict - nothing was installed.
  greeter from Contoso.AgentSkills@1.2.0 conflicts with 'greeter' from acme/skills (github) in the project scope.
Keep both by installing under another name (add your other options as before):
  agentskills-cli add Contoso.AgentSkills --as greeter=greeter-contoso
Or replace the existing skill(s):
  agentskills-cli add Contoso.AgentSkills --replace
```

### Installing under another name

`--as greeter=greeter-contoso` installs the skill into `greeter-contoso/` and sets `name: greeter-contoso` in the installed `SKILL.md`, because the Agent Skills spec requires a skill's `name` to match its folder and agents load skills by that name. Your agent sees it as `greeter-contoso`. The source is never modified.

The [project or global lock](/reference/lock-files#aliases) records the entry under `greeter-contoso` with `"originalName": "greeter"`, so:

- [`update`](/commands/update) and [`restore`](/commands/restore) find the skill in its source by its original name and reinstall it under the alias.
- Re-adding the same source later reuses the alias automatically.
- `list` shows it as `greeter-contoso (alias of greeter)`, and `remove greeter-contoso` removes it.

```bash
# Keep both
agentskills-cli add Contoso.AgentSkills --as greeter=greeter-contoso -y

# Several skills, several aliases
agentskills-cli add Contoso.AgentSkills --as greeter=greeter-contoso --as linter=linter-contoso -y

# One skill selected, so the skill name can be left out
agentskills-cli add Contoso.AgentSkills -s greeter --as greeter-contoso -y
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

1. **Source-type conventions** - `skills/` at the package root for NuGet (then the older `contentFiles/any/any/skills/`), `package/skills/` then `package/contentFiles/...` for npm, the priority directory list for git.
2. **Recursive scan** up to 5 levels deep, skipping `node_modules`, `.git`, `dist`, `build`, `__pycache__`.
3. With `--path`, the scan is restricted to that one subdirectory of the staged source.

## What gets written

- Skill files copied (or symlinked) to the install paths documented in [Where files land](/reference/where-files-land)
- Global lock entry in `~/.agents/.skill-lock.json` (global installs, `-g`)
- Project lock entry in `./skills-lock.json` (project installs)

See [Lock files](/reference/lock-files) for the schema details.

## Result statuses

| Status in the Result column | Means |
|---|---|
| `copied` | Files materialized into the agent directory. |
| `symlinked` | Agent directory symlinked to the canonical `.agents/skills` copy. |
| `copy (symlink fell back)` | `--symlink` was requested but the filesystem refused; copied instead. |
| `canonical only` | Skill placed in the canonical `.agents/skills/`, but the agent-specific directory was deliberately skipped (project install, non-universal agent, agent's root config dir doesn't exist in the project). See [Where files land](/reference/where-files-land) for the rule. |
| `failed: <reason>` | Install couldn't complete; the row reports why. |
