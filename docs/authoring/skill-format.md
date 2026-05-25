# SKILL.md format

The atomic unit of a skill is a folder containing a `SKILL.md` file with YAML frontmatter. The format is defined by the open [Agent Skills specification](https://agentskills.io); AgentSkills CLI implements it faithfully.

## Minimum

```markdown
---
name: my-skill
description: One-line hint that helps an agent decide when to use this skill.
---
# my-skill

Body markdown. This is what the agent reads.
```

Two frontmatter fields are required: `name` and `description`. Both must be strings.

## Frontmatter schema

```yaml
---
name: <slug>             # required, string. Used as the install directory name (kebab-case).
description: <one line>  # required, string. Shown in lists and to agents at discovery time.
metadata:                # optional, free-form object.
  internal: false        # if true, hidden unless INSTALL_INTERNAL_SKILLS=1 or user names it explicitly.
  any: thing             # arbitrary fields passed through to the installer.
---
```

### `name`

Used as the install directory name. Run through a kebab-case sanitizer (lowercased, non-alphanumeric runs collapsed to `-`, leading/trailing dots and hyphens stripped, capped at 255 chars).

This means `My Cool Skill!` and `my-cool-skill` install to the same directory. The displayed name in `list` output preserves the original casing where possible.

### `description`

One-line hint shown in:

- `agentskills-cli list` output (truncated to ~80 chars)
- `agentskills-cli find` results
- The multi-select prompt during `add`
- Agent-side discovery (some agents read this directly to decide which skills to apply)

Keep it action-oriented. Good: "Run a database migration with safety checks." Bad: "Database stuff."

### `metadata`

Free-form. Two known fields:

| Field | Effect |
|---|---|
| `metadata.internal: true` | Hides the skill from default discovery. To install, the user must set `INSTALL_INTERNAL_SKILLS=1` OR pass `-s <skill-name>` explicitly. Useful for skills that are dependencies of others, or experimental ones. |

Anything else under `metadata` is passed through to the installer and ignored unless your tooling reads it.

## Body content

The body (everything after the closing `---`) is what AI agents actually read. Markdown is the universal format. Conventions across the agent-skills ecosystem:

- Lead with the **purpose** in a sentence or two
- Add a `## When to invoke` section with concrete trigger conditions
- Add a `## Notes` or `## Reference` section for things the agent should know
- Keep it concise - agents have limited context windows

## Multi-skill collections

A single source (git repo, NuGet package, npm package) can carry many skills. Each subfolder with a `SKILL.md` is independently installable.

```
my-skills-repo/
└── skills/
    ├── skill-one/SKILL.md
    ├── skill-two/SKILL.md
    └── skill-three/
        ├── SKILL.md
        └── reference.md
```

Discovery scans the conventional priority directories first, then recurses up to 5 levels deep, skipping `node_modules`, `.git`, `dist`, `build`, `__pycache__`.

## Excluded from copy

When AgentSkills CLI installs a skill, it copies the contents of the SKILL folder but excludes:

- `metadata.json` (a separate file - rare, but reserved)
- `.git/`
- `__pycache__/`
- `__pypackages__/`

Broken symlinks are skipped silently rather than aborting the install.

## Validation

AgentSkills CLI validates frontmatter on parse:

- Missing `name` or `description` → skill ignored (not installed, no error)
- Wrong types (e.g., `name: 42`) → skill ignored
- Body without frontmatter → skill ignored (the YAML `---` block is required)

If `agentskills-cli add` reports "No SKILL.md files found", check that your SKILL.md has both a proper `---` delimited frontmatter AND the two required fields.

## Next

- [Publishing to NuGet →](/authoring/publishing-nuget)
- [Publishing to npm →](/authoring/publishing-npm)
