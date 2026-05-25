# Concepts

The five terms you need to know.

## Skill

A folder with a `SKILL.md` at the root:

```
my-skill/
├── SKILL.md          # required, with YAML frontmatter
├── reference.md      # optional, anything else the skill needs
└── prompts/...
```

`SKILL.md` frontmatter must include `name` and `description`:

```markdown
---
name: my-skill
description: One-line hint that helps an agent decide when to use this skill.
---
# my-skill

Body markdown. This is what the agent reads.
```

The format is defined by the open [Agent Skills specification](https://agentskills.io).

## Skill collection

A **source** can carry one skill or many. Each child folder with a `SKILL.md` is its own independently-installable skill:

```
my-skills-repo/                    # a single source (git repo, .nupkg, .tgz, …)
└── skills/
    ├── skill-one/SKILL.md
    ├── skill-two/SKILL.md
    └── skill-three/
        ├── SKILL.md
        └── reference.md
```

`agentskills-cli add <source>` discovers every skill in the source and (by default) installs all of them. Narrow the set with `-s <name>` to pick specific skills, or pick interactively with the multi-select prompt.

## Source

Where a skill collection comes from:

- A **local folder**
- A **git repo** (GitHub shorthand, GitHub/GitLab URLs, arbitrary git URLs)
- A **NuGet package** (public or private feed)
- An **npm package** (public or private registry)
- An **HTTPS endpoint** that follows the well-known discovery convention

See [Source formats](/sources/) for all the input shapes the CLI accepts.

## Agent

A target tool that reads skills. AgentSkills CLI v1 ships five agent targets:

| Agent | What it reads |
|---|---|
| `claude-code` | `.claude/skills/` |
| `codex` | `.agents/skills/` (and `~/.codex/skills/` globally) |
| `cursor` | `.agents/skills/` |
| `opencode` | `.agents/skills/` |
| `universal` | `.agents/skills/` (the canonical location every spec-compliant agent reads) |

AgentSkills CLI knows where each agent looks and copies the skill there.

## Scope

- **Project install** (default): skill files land in the current directory (`./.agents/skills/…` or `./.claude/skills/…` depending on the agent).
- **Global install** (`-g`): skill files land under your home directory (`~/.agents/skills/…` or the agent's home dir).

Project installs are version-controlled with the project (the project lock file ships in the repo). Global installs follow you across every project on the machine.

## Universal vs per-agent agents

Cursor, Codex, OpenCode and the "universal" target all read from the same canonical `.agents/skills/` directory. Installing a skill for one of them effectively installs it for all of them.

Claude Code has its own `.claude/skills/` directory. On filesystems that support it, AgentSkills CLI symlinks `.claude/skills/<name>` to the canonical copy in `.agents/skills/<name>`; otherwise it copies.

This means: installing a skill once typically makes it available to **every coding agent the developer uses**, not just the one specified on the command line.

Next: [Quick start →](/getting-started/quick-start)
