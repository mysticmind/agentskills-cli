# Source formats

AgentSkills accepts a wide range of input shapes. Detection is order-sensitive - the first rule that matches wins.

| Input shape | Source type | Notes |
|---|---|---|
| `./foo`, `/abs/path`, `.`, `..`, `C:\…` | [Local folder](/sources/local) | Resolved absolute path |
| `owner/repo[/subpath][#ref][@skill]` | [GitHub](/sources/github) shorthand | Most common |
| `https://github.com/owner/repo[/tree/<ref>/<path>]` | [GitHub](/sources/github) URL | URL form, same destination |
| `github:owner/repo` | [GitHub](/sources/github) prefix | Explicit |
| `https://gitlab.com/group/[subgroup/]repo[/-/tree/<ref>/<path>]` | [GitLab](/sources/gitlab) | Subgroups supported |
| `gitlab:group/repo` | [GitLab](/sources/gitlab) prefix | Explicit |
| `git@host:org/repo.git` or `https://host/org/repo.git` | [Arbitrary git URL](/sources/git) | Any git host |
| `PackageId[@version]` (with a `.` in the id, no slash) | [NuGet](/sources/nuget) shorthand | Auto-detected |
| `nuget:PackageId[@version]` | [NuGet](/sources/nuget) prefix | Explicit; required for IDs without a `.` |
| `@scope/name[@version]` | [npm](/sources/npm) scoped shorthand | Auto-detected from leading `@` |
| `npm:<id>[@version]` | [npm](/sources/npm) prefix | Required for unscoped npm names |
| `https://example.com/…` (non-git host) | [Well-known endpoint](/sources/well-known) | Probes `/.well-known/agent-skills/index.json` |

## The conventional layouts

Most of the time, sources just work because they follow one of the conventional layouts AgentSkills probes first:

- **NuGet**: `contentFiles/any/any/skills/<name>/SKILL.md`
- **npm**: `package/skills/<name>/SKILL.md` (tarball roots at `package/`)
- **git / local**: any of the priority directories: `skills/`, `.agents/skills/`, `.claude/skills/`, `.cursor/skills/`, `.codex/skills/`, … (full list in source-discovery code)

When a source doesn't follow the convention, AgentSkills falls back to a **recursive scan up to 5 levels deep**, skipping `node_modules`, `.git`, `dist`, `build`, `__pycache__`.

When that's still not enough (skills live deeper than 5 levels, or you want to restrict discovery to a specific subdirectory), use the [`--path`](/commands/add) flag.

## Auth

All auth is read from the ecosystem's own config files - AgentSkills doesn't introduce any new auth surface.

| Source type | Auth source |
|---|---|
| GitHub / GitLab / git | Whatever your local `git` is configured with (SSH key, git-credential-manager, etc.) |
| NuGet | `NuGet.Config` (machine + user + project) and any installed [credential providers](https://learn.microsoft.com/en-us/nuget/reference/extensibility/nuget-cross-platform-plugins) |
| npm | `~/.npmrc` and project `.npmrc` (default registry, scoped registries, `_authToken`, `_auth`, `${ENV_VAR}` expansion) |
| Well-known | None typically (HTTPS); if you need auth, run an HTTPS endpoint that handles it |

If `dotnet restore` works against your NuGet feed, `agentskills add <pkg>` works against the same feed. Same for `npm install` and npm packages.
