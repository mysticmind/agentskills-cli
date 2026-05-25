# Well-known endpoints

For organizations that want to publish skills via their own HTTPS endpoint without using a package manager. Implements the open [Agent Skills well-known discovery spec](https://agentskills.io).

## Input shape

```bash
agentskills-cli add https://skills.example.com
agentskills-cli add https://skills.example.com/team
```

Any HTTPS URL whose hostname is **not** github.com/gitlab.com/raw.githubusercontent.com falls into this bucket.

## How it works

AgentSkills probes for an index at:

1. `<url>/.well-known/agent-skills/index.json` (modern path)
2. `<url>/.well-known/skills/index.json` (legacy fallback)

If both fail, falls back to root-domain probing for the same two paths.

## Index format

Two schema versions are supported:

### v0.2.0 (current spec)

JSON manifest declaring per-skill artifacts with mandatory SHA-256 digest verification:

```json
{
  "$schema": "https://schemas.agentskills.io/discovery/0.2.0/schema.json",
  "skills": [
    {
      "name": "greeter",
      "description": "Says hello.",
      "type": "skill-md",
      "url": "greeter.md",
      "digest": "sha256:abc123…"
    },
    {
      "name": "archived",
      "description": "An archive with files.",
      "type": "archive",
      "url": "archived.zip",
      "digest": "sha256:def456…"
    }
  ]
}
```

Two artifact types:

- `skill-md` - a single `SKILL.md` file
- `archive` - a `.zip` or `.tar.gz` containing the full skill directory

Archives are validated and extracted with:

- 50 MB max unpacked size
- 1000 files max
- No symlinks or hard links
- No `..` segments or absolute paths

After download, the digest is verified before extraction. A mismatch aborts the install.

### v0.1.0 (legacy)

Older directory-style format:

```json
{
  "skills": [
    {
      "name": "hello",
      "description": "hi",
      "files": ["SKILL.md", "ref/notes.md"]
    }
  ]
}
```

Each `files` entry is fetched individually from `<base>/<wellknown>/<name>/<file>`.

## Schema version detection

The `$schema` URL is the version marker:

| `$schema` value | Parsed as |
|---|---|
| `https://schemas.agentskills.io/discovery/0.2.0/schema.json` | v0.2.0 |
| (absent) | v0.1.0 (legacy) |
| Anything else | Rejected (future-proofing against unknown schema versions) |

## When to use

Well-known endpoints make sense when:

- You have a corporate HTTPS endpoint and want skill discovery without standing up a package registry
- You want to publish under a vanity domain (e.g., `skills.yourcompany.com`)
- You're integrating with an existing CMS that can serve JSON + files
