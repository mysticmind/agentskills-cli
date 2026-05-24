# `list`

Inspect installed skills.

## Synopsis

```
agentskills list [<target>...] [-g] [-a agent...] [--by package|path|agent|scope] [--paths]
```

## Arguments

| Argument | Meaning |
|---|---|
| `<target>` | Optional filter. Each argument is matched first as a skill name, then as a source. "Source" accepts any shape `add` accepts (local paths, GitHub `owner/repo` or URL, GitLab URL, NuGet package id, npm package id, arbitrary git URL). Version suffix ignored. Repeatable; the match is a union. Omit to list everything. |

## Flags

| Flag | Meaning |
|---|---|
| `-g`, `--global` | Show only globally installed skills. Default shows both project and global. |
| `-a`, `--agent <NAME>` | Limit the view to specific agents. |
| `--by <KEY>` | Group the output. Values: `package`, `path`, `agent`, `scope`. Default is a single flat table. |
| `--paths` | Add a column with each skill's on-disk install path. |

## Examples

```bash
# Filter by skill name
agentskills list hello-skill

# Filter by source - same parsing as `add`
agentskills list @acme/sample-skills                           # npm scoped
agentskills list npm:sample-pkg                                # npm unscoped
agentskills list MyOrg.AgentSkills                             # NuGet
agentskills list anthropics/skills                             # GitHub shorthand
agentskills list https://github.com/anthropics/skills          # GitHub URL - same lock entries
agentskills list https://gitlab.com/group/sub/repo             # GitLab
agentskills list /abs/path/to/local/skill                      # local path

# Mix skill names and sources - the union is shown
agentskills list hello-skill anthropics/skills MyOrg.AgentSkills

# Group by package (one mini-table per source)
agentskills list --by package

# Group by install directory + show the full path on each row
agentskills list --by path --paths

# Group by agent or scope
agentskills list --by agent
agentskills list --by scope
```

## Output

The default flat table:

```
╭────────────────┬─────────┬───────────────────────────┬─────────────────────┬─────────────────╮
│ Skill          │ Scope   │ Agents                    │ Source              │ Description     │
├────────────────┼─────────┼───────────────────────────┼─────────────────────┼─────────────────┤
│ hello-skill    │ project │ codex, cursor, opencode,  │ /path/to/source     │ A minimal …     │
│                │         │ universal                 │ (local)             │                 │
│ web-design-…   │ global  │ claude-code               │ vercel-labs/agent-… │ Web design …    │
│                │         │                           │ (github)            │                 │
╰────────────────┴─────────┴───────────────────────────┴─────────────────────┴─────────────────╯
```

Skills not tracked in any lock (installed manually or before lock tracking) appear under `(untracked)` when `--by package` is set, and with a `-` in the Source column otherwise.

## Versions

For NuGet and npm targets, dropping the `@version` matches any installed version; pinning a version (`@acme/sample-skills@1.5.0`) requires an exact match.

If the requested version isn't installed but a different version is, the command prints a hint:

```
$ agentskills list npm:@acme/sample-skills@1.5.0
No installed skills matched npm:@acme/sample-skills@1.5.0.
hint: @acme/sample-skills is installed at version(s) 1.4.0; drop the @version to list anyway.
```

GitHub refs (`anthropics/skills#main`) are not version constraints - the lock tracks them separately, so they're ignored when matching.
