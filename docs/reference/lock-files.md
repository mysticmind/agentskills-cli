# Lock files

AgentSkills CLI writes two lock files so installs are reproducible and [`update`](/commands/update) has something to diff against.

## Global lock - `~/.agents/.skill-lock.json`

Located at `$XDG_STATE_HOME/skills/.skill-lock.json` when that variable is set, otherwise `~/.agents/.skill-lock.json`.

Schema v3, sorted by skill name, one entry per globally-installed skill:

```json
{
  "version": 3,
  "skills": {
    "web-design-guidelines": {
      "source": "vercel-labs/agent-skills",
      "sourceType": "github",
      "sourceUrl": "https://github.com/vercel-labs/agent-skills.git",
      "skillPath": "skills/web-design-guidelines/SKILL.md",
      "skillFolderHash": "3116f3e62dbd02b44a598b1aa690d2a8938e8f89",
      "installedAt": "2026-05-23T14:24:05.96Z",
      "updatedAt": "2026-05-23T14:24:05.96Z"
    }
  }
}
```

| Field | Meaning |
|---|---|
| `source` | The package/repo locator: `owner/repo` for git, `<id>@<version>` for NuGet/npm |
| `sourceType` | One of: `local`, `github`, `gitlab`, `git`, `nuget`, `npm`, `well-known` |
| `sourceUrl` | The actual URL the source was fetched from |
| `ref` | Git ref (branch/tag) if specified |
| `skillPath` | Path of the SKILL.md inside the source (e.g., `skills/foo/SKILL.md`) |
| `skillFolderHash` | Change-detection baseline used by `update`: the git tree SHA of the skill's folder for git sources (GitHub, GitLab, generic git), otherwise the SHA-256 content hash of the folder. |
| `installedAt` | ISO-8601 timestamp |
| `updatedAt` | ISO-8601 timestamp - same as `installedAt` until `update` runs |

[`update`](/commands/update) reads this file to know what to check for remote drift.

## Project lock - `./skills-lock.json`

Schema v1, sorted alphabetically (for clean git diffs), **commit this to your repo**:

```json
{
  "version": 1,
  "skills": {
    "web-design-guidelines": {
      "source": "vercel-labs/agent-skills",
      "sourceType": "github",
      "skillPath": "skills/web-design-guidelines/SKILL.md",
      "computedHash": "f3bc47f890f42a44db1007ab390709ec368e4b8c089baee6b0007182236ac474"
    }
  }
}
```

| Field | Meaning |
|---|---|
| `source`, `sourceType`, `ref`, `skillPath` | Same as global lock |
| `computedHash` | SHA-256 over the installed skill folder (relative path + bytes). Independent of git. Useful for CI to verify on-disk state matches what's tracked. |
| `sourceUrl` | Where the source was fetched from: the NuGet feed or npm registry, the clone URL for git sources, or a `file://` URI for a local package file. `update` and `restore` go back there first. A folder inside the project is stored relative to the project root. Omitted for local skill folders. |

The project lock is written on every project install. Skipped for global installs (those only touch the global lock). [`restore`](/commands/restore) reinstalls everything it lists.

## Aliases

A skill installed under another name ([`add --as`](/commands/add#installing-under-another-name)) is keyed by the name it was installed under, and the entry records the skill's name in its source:

```json
"greeter-contoso": {
  "source": "Contoso.AgentSkills@1.2.0",
  "sourceType": "nuget",
  "originalName": "greeter",
  "computedHash": "..."
}
```

`originalName` appears in both lock files and is omitted for skills installed under their own name. `vercel-labs/skills` doesn't know this field, so it can't update or restore aliased skills, but it leaves the rest of the lock usable.

## Fields from other tools

Fields AgentSkills CLI doesn't recognize are kept when it rewrites a lock entry, so a newer tool's (or upstream's) additions aren't lost.

## Which lock is written

Each scope has exactly one lock: global installs (`-g`) write only the global lock, project installs write only `./skills-lock.json`. `remove` and `update` change only the lock of the scope they act on.

Before 1.1.0, project installs also added an entry to the global lock. `update` reports those leftovers as stale entries and removes them; `outdated` lists them without changing anything.

## Safety

- **Corrupt file**: an unparseable lock is copied to `<lock>.corrupt-<timestamp>`, a warning is printed, and the lock is treated as empty. The original is only replaced on the next successful write.
- **Newer format**: a lock with a higher `version` than this tool understands is read but never overwritten; commands that would change it fail with an upgrade hint.
- **Older global format** (before v3): reset to an empty v3 lock, matching upstream, after saving a copy to `<lock>.v<N>-<timestamp>`.
- **Atomic writes**: locks are written to a temp file and moved into place, so an interrupted run can't leave a half-written file.

## Interop with `vercel-labs/skills`

These schemas match the `vercel-labs/skills` lock format exactly. The two tools can share state - a skill installed via `npx skills add` is visible to `agentskills-cli list` and `update`, and vice versa.

This is deliberate. The lock format isn't part of the published spec at agentskills.io, but matching the existing convention means users can use either tool interchangeably without losing tracked state.

## Lock-file safety

Both files are written atomically (write to temp + rename) and sorted deterministically. Committing the project lock and seeing predictable diffs is part of the expected workflow.

Manual edits work but aren't recommended - prefer `agentskills-cli add` / `agentskills-cli remove` to keep state consistent.
