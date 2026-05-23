# Changelog

All notable changes are documented here. Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project adheres to [SemVer](https://semver.org). API may evolve in any `0.x` release.

## 0.2.0 - 2026-05-23

The first public release of **AgentSkills**, a .NET port of [vercel-labs/skills](https://github.com/vercel-labs/skills) implementing the open [Agent Skills spec](https://agentskills.io).

### Sources

- **Local folders** - `agentskills add ./my-skill`
- **GitHub** - shorthand (`owner/repo[/subpath][#ref][@skill]`), full URLs (incl. `/tree/<ref>/<path>`), `github:` prefix
- **GitLab** - shorthand and URL (incl. `/-/tree/<ref>/<path>`), `gitlab:` prefix, subgroups
- **Arbitrary git URL** - HTTPS / SSH, via system `git`
- **NuGet** - public *and* private feeds; reads `NuGet.config` + credential providers (`nuget:PackageId[@version]` or bare `PackageId[@version]`)
- **npm** - public *and* private registries; reads `~/.npmrc` + project `.npmrc` for default + per-scope registries and `_authToken`/`_auth` (`npm:<id>` or `@scope/name[@version]`)
- **Well-known endpoints** (RFC 8615-style) - `/.well-known/agent-skills/index.json` with `/.well-known/skills/` legacy fallback; v0.2.0 schema with SHA-256 verification and v0.1.0 directory model; archives can be `.zip` or `.tar.gz`

### Commands

| Command | What it does |
|---|---|
| `add` | Install one or more skills from a source. Captures GitHub tree SHA per skill so `update` works. |
| `list` | Inspect installed skills. Positional targets match by skill name *or* any source format. `--by package\|path\|agent\|scope` to group, `--paths` for the install path column. |
| `remove` | Remove installed skills. Positional targets match by skill name *or* any source format (e.g. `agentskills remove @acme/sample-skills -y` wipes everything from that package). |
| `init` | Scaffold a `SKILL.md` template. |
| `find` | Search [skills.sh](https://skills.sh), pick a result interactively, hand off to `add`. |
| `update` | Use the GitHub Trees API to detect drift, reinstall changed skills. Lazy GitHub auth (`GITHUB_TOKEN` → `GH_TOKEN` → `gh auth token`). |

### Agents (v1 set)

`claude-code`, `codex`, `cursor`, `opencode`, `universal` - auto-detected by default; pick explicitly with `-a`.

### Runtime

- **Multi-targeted: `net8.0` and `net10.0`** - `dotnet tool install --global AgentSkills` works on either runtime; `dnx AgentSkills` is .NET 10 only (because `dnx` itself ships with the .NET 10 SDK).

### Versioning of package targets

- `agentskills list MyOrg.AgentSkills` matches every installed version.
- `agentskills list MyOrg.AgentSkills@1.2.3` strict-matches; a "did you mean…" hint is printed if a different version is installed.
- Scoped npm (`@acme/sample-skills@1.0.0`) is parsed correctly - the leading `@` is the scope marker, not a version.

### Output / UX

- Spectre.Console throughout - banner, tables, multi-selects, status spinners, install table now includes a `Path` column + `Installed under …` summary.
- All third-party text (skill descriptions, package ids, search API responses) is escaped before being rendered as markup, so `[Entity]` and friends don't crash the prompt parser.

### Known limitations (will revisit before 1.0)

- Only 5 of the upstream's 55 agents shipped - the rest can land incrementally.
- `update` only handles GitHub via the Trees API; GitLab/git/well-known are reported as "skipped" with a reason.
- No telemetry, no `experimental_install` / `experimental_sync` commands.
- No tab completion script yet.
- `find` UI is a two-step prompt (text → selection), not a live fzf-style search.
