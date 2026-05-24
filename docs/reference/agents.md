# Supported agents

v1 ships five agent targets:

| `-a` name | Display | Project dir | Global dir | Universal? |
|---|---|---|---|---|
| `claude-code` | Claude Code | `.claude/skills` | `$CLAUDE_CONFIG_DIR/skills` or `~/.claude/skills` | no |
| `codex` | Codex | `.agents/skills` | `$CODEX_HOME/skills` or `~/.codex/skills` | yes |
| `cursor` | Cursor | `.agents/skills` | `~/.cursor/skills` | yes |
| `opencode` | OpenCode | `.agents/skills` | `$XDG_CONFIG_HOME/opencode/skills` or `~/.config/opencode/skills` | yes |
| `universal` | Universal | `.agents/skills` | `$XDG_CONFIG_HOME/agents/skills` or `~/.config/agents/skills` | yes |

## Universal vs per-agent

Cursor, Codex, OpenCode and the "universal" target share the canonical `.agents/skills` directory. Installing a skill for one of them is effectively installing for all of them - no duplicate copies, no extra disk space.

Claude Code is the only **non-universal** agent in v1 - it has its own `.claude/skills/` directory. On supported filesystems we symlink it to the canonical copy; otherwise we copy.

## Auto-detection

If you don't pass `-a`, AgentSkills auto-detects which agents are installed on the system (by checking for their config directories) and, in interactive mode, presents a multi-select. Universal agents are pre-selected and always included.

## Why only five?

The `npx skills` tool lists 55 agents. AgentSkills v1 ships the most commonly used ones to keep the surface small. Adding more is mechanical work; if you need a specific agent, open an issue or PR with the agent's name and conventional skills directory.

## Adding a custom agent

The agent registry is currently a static class. Future versions may expose it as an extension point similar to [search providers](/reference/search-providers). For now, custom agents require a code change.
