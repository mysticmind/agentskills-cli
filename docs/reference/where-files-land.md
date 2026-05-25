# Where files land

A skill folder is always copied (or symlinked, with `--symlink`) into one of two locations, derived deterministically from the `(scope, agent)` pair.

## Project scope (default - no `-g`)

With `$PROJECT` = current working directory:

| Agent | Install path |
|---|---|
| `claude-code` | `$PROJECT/.claude/skills/<skill-name>/` |
| `codex`, `cursor`, `opencode`, `universal` | `$PROJECT/.agents/skills/<skill-name>/` (shared canonical dir) |

## Global scope (`-g`)

With `$HOME` = user home (env vars take precedence if set):

| Agent | Install path |
|---|---|
| `claude-code` | `$CLAUDE_CONFIG_DIR/skills/<skill-name>/` or `$HOME/.claude/skills/<skill-name>/` |
| `codex` | `$CODEX_HOME/skills/<skill-name>/` or `$HOME/.codex/skills/<skill-name>/` |
| `cursor` | `$HOME/.cursor/skills/<skill-name>/` |
| `opencode` | `$XDG_CONFIG_HOME/opencode/skills/<skill-name>/` or `$HOME/.config/opencode/skills/<skill-name>/` |
| `universal` | `$XDG_CONFIG_HOME/agents/skills/<skill-name>/` or `$HOME/.config/agents/skills/<skill-name>/` |

## `<skill-name>` sanitization

The `<skill-name>` segment is the kebab-case form of the SKILL.md `name` field: lowercased, runs of non-`[a-z0-9._]` collapsed to `-`, leading/trailing dots and hyphens stripped, capped at 255 chars.

## Three ways to see paths for skills already on disk

1. [`agentskills-cli add …`](/commands/add) prints them in the result table's `Path` column and in the `Installed under …` summary
2. [`agentskills-cli list --paths`](/commands/list) re-renders the same `Path` column for everything installed
3. [`agentskills-cli list --by path --paths`](/commands/list) groups skills by install directory - handy for a "what's actually in `~/.claude/skills/`?" view

## The "canonical only" install result

You may see `canonical only` in the result column when adding for a non-universal agent (typically `claude-code`) without that agent's config directory existing in the project. In that case AgentSkills CLI installs only to the canonical `.agents/skills/` location and skips creating the per-agent mirror. This avoids polluting your project with config directories for agents you may not actually use.

The skill is still available - any future `agentskills-cli add` (or symlink) will materialize the agent-specific copy if the config directory appears later.
