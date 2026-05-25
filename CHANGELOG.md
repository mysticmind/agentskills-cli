# Changelog

All notable changes are documented here. Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project adheres to [SemVer](https://semver.org). API may evolve in any `0.x` release.

## 0.3.0 - 2026-05-26

_In development - changes since `0.2.0` accumulate here as they land._

### Housekeeping (post-0.2.0)

- `LICENSE`: dropped the "Portions Copyright (c) Vercel Labs..." line. AgentSkills CLI is a clean-room C# port with no source code copied, so MIT's notice clause isn't triggered. Attribution now lives in `NOTICE` where it belongs.
- `NOTICE`: rewritten to use the current product name + a proper attribution block. Names what's shared with upstream (behavior, design, lock format) and what isn't (source code).
- README badges: NuGet + GitHub release badges now track stable releases only (not prereleases) and use shields.io default colors instead of the custom indigo.

## 0.2.0 - 2026-05-26

The first public release of **AgentSkills CLI**, a .NET commandline tool for the open [Agent Skills](https://agentskills.io) ecosystem. A faithful .NET-native port of [vercel-labs/skills](https://github.com/vercel-labs/skills) (the `npx skills` CLI) - same `SKILL.md` format, same lock files, same `.agents/skills/` install directory, so skills installed by either tool are visible to the other and toolchains can be mixed freely.

### Sources

- **Local folders** - `agentskills-cli add ./my-skill`
- **Local `.nupkg` files** - `agentskills-cli add ./Contoso.SampleSkills.1.4.0.nupkg`. Extracts the file directly; no feed lookup needed. Package id + version come from the embedded `.nuspec`.
- **Local npm tarballs** - `agentskills-cli add ./contoso-sample-skills-2.1.0.tgz`. Same behavior for `.tgz` and `.tar.gz`; name + version come from `package/package.json`.
- **GitHub** - shorthand (`owner/repo[/subpath][#ref][@skill]`), full URLs (incl. `/tree/<ref>/<path>`), `github:` prefix.
- **GitLab** - shorthand and URL (incl. `/-/tree/<ref>/<path>`), `gitlab:` prefix, subgroups.
- **Arbitrary git URL** - HTTPS / SSH, via system `git`.
- **NuGet** - public *and* private feeds; reads `NuGet.config` + credential providers (`nuget:PackageId[@version]` or bare `PackageId[@version]`).
- **npm** - public *and* private registries; reads `~/.npmrc` + project `.npmrc` for default + per-scope registries and `_authToken` / `_auth` (`npm:<id>` or `@scope/name[@version]`).
- **Well-known endpoints** (RFC 8615-style) - `/.well-known/agent-skills/index.json` with `/.well-known/skills/` legacy fallback; v0.2.0 schema with SHA-256 verification and v0.1.0 directory model; archives can be `.zip` or `.tar.gz`.

### Commands

| Command | What it does |
|---|---|
| `add` | Install one or more skills from a source. Captures GitHub tree SHA per skill so `update` works. |
| `list` | Inspect installed skills. Positional targets match by skill name *or* any source format. `--by package\|path\|agent\|scope` to group, `--paths` for the install path column. |
| `remove` | Remove installed skills. Positional targets match by skill name *or* any source format (e.g. `agentskills-cli remove @acme/sample-skills -y` wipes everything from that package). |
| `init` | Scaffold a `SKILL.md` template. |
| `find` | Search [skills.sh](https://skills.sh), pick a result interactively, hand off to `add`. |
| `update` | Use the GitHub Trees API to detect drift, reinstall changed skills. Lazy GitHub auth (`GITHUB_TOKEN` -> `GH_TOKEN` -> `gh auth token`). |

### Agents (v1 set)

`claude-code`, `codex`, `cursor`, `opencode`, `universal` - auto-detected by default; pick explicitly with `-a`.

### Versioned install units

Skills from a NuGet or npm package are tracked together as a managed set:

- `agentskills-cli add MyOrg.SkillPack -y` installs all of them in one shot.
- `agentskills-cli remove MyOrg.SkillPack -y` wipes them all.
- `agentskills-cli update` checks the whole package for drift.
- `agentskills-cli list --by package` groups them.
- No orphan skills when you uninstall.

### Runtime

- **Multi-targeted: `net8.0` and `net10.0`** - `dotnet tool install --global agentskills-cli` works on either runtime; `dnx agentskills-cli` is .NET 10 only (because `dnx` itself ships with the .NET 10 SDK). Daily-use command is `agentskills-cli` regardless of how it was installed.

### Versioning of package targets

- `agentskills-cli list MyOrg.AgentSkills` matches every installed version.
- `agentskills-cli list MyOrg.AgentSkills@1.2.3` strict-matches; a "did you mean..." hint is printed if a different version is installed.
- Scoped npm (`@acme/sample-skills@1.0.0`) is parsed correctly - the leading `@` is the scope marker, not a version.

### Output / UX

- Spectre.Console throughout - banner, tables, multi-selects, status spinners, install table includes a `Path` column + `Installed under ...` summary.
- All third-party text (skill descriptions, package ids, search API responses) is escaped before being rendered as markup, so `[Entity]` and friends don't crash the prompt parser.

### Engineering

- 0 build warnings, `TreatWarningsAsErrors`, NetAnalyzers + Meziantou.Analyzer at Recommended.
- 128 tests passing on net8.0 + net10.0.
- DI throughout (`Microsoft.Extensions.DependencyInjection`); typed exception hierarchy; structured logging via `Microsoft.Extensions.Logging`.

### Extension points

- Register `ISkillSourceFactory` for new source types (cargo, oci, conda, your internal feed) with a single DI registration.
- Register `ISkillSearchProvider` for new search backends (internal registries, GitHub topic search) the same way.

### Known limitations (will revisit before 1.0)

- Only 5 of the broader 55-agent registry shipped - the rest can land incrementally.
- `update` only handles GitHub via the Trees API; GitLab/git/well-known are reported as "skipped" with a reason.
- No telemetry, no `experimental_install` / `experimental_sync` commands.
- No tab completion script yet.
- `find` UI is a two-step prompt (text -> selection), not a live fzf-style search.
