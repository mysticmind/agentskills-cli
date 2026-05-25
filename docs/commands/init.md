# `init`

Scaffold a new `SKILL.md` template in a directory.

## Synopsis

```
agentskills-cli init [PATH] [-y]
```

## Arguments

| Argument | Meaning |
|---|---|
| `PATH` | Directory to scaffold in. Defaults to the current directory. Created if missing. |

## Flags

| Flag | Meaning |
|---|---|
| `-y`, `--yes` | Overwrite an existing `SKILL.md` without asking. |

## Examples

```bash
# Scaffold in current directory
agentskills-cli init

# Scaffold in a new directory
agentskills-cli init my-new-skill -y
```

## What gets created

```
<path>/SKILL.md
```

Content:

```markdown
---
name: <derived-from-directory-name>
description: One-line description that helps an agent decide when to use this skill.
---
# <derived-from-directory-name>

What this skill does and how it should be used by an agent.

## When to invoke

- Bullet trigger conditions

## Notes

- Anything else the agent should know.
```

The `name` field is derived from the directory name and run through the same kebab-case sanitizer the installer uses (lowercased, non-alphanumeric runs collapsed to `-`, leading/trailing dots and hyphens stripped, capped at 255 chars). Edit it to match whatever name agents should use to refer to the skill.

## Overwrite behavior

If `SKILL.md` already exists in the target directory:

- Without `-y`: prompts `<path>/SKILL.md exists. Overwrite?`
- With `-y`: overwrites silently

The original file is not backed up. Commit before running with `-y` if the existing file is precious.
