# Changelog

All notable changes are documented here. Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project adheres to [SemVer](https://semver.org).

## 1.1.0 - Unreleased

Package-level updates for every source type, a `restore` command, name-conflict aliases, version reporting, and fixes to how the lock files track scopes. Lock file formats stay compatible with `vercel-labs/skills`; the only addition is the optional `originalName` field. The package still ships both `net8.0` and `net10.0` builds.

### Behavior changes

These fix unsafe or surprising behavior; check them if you script the tool.

- **`add -y` stops on a name conflict** instead of silently replacing a skill from another source. Nothing is installed and the exit code is `2`. Pass `--replace` to overwrite, or `--as <skill>=<new-name>` to keep both.
- **`check` is now an alias of the read-only `outdated` command.** It used to be an alias of `update` and could install updates.
- **Project installs no longer write to the global lock.** Tools or scripts that read `~/.agents/.skill-lock.json` to find project skills must read `./skills-lock.json` instead.
- **`--json` output** is new on `list`, `outdated`, `update` and `version`; scripts that parsed the tables should switch to it.

### New

- **`update` works for every source type.** NuGet and npm skills are checked against your feeds and registries (using your `NuGet.config` and `.npmrc`) and moved to the newest version. GitLab, generic git, well-known, and local sources are checked by fetching them and comparing the skill folder. Before, only global GitHub skills were checked; everything else was reported as skipped.
- **`update` and `outdated` accept packages and sources as targets**, the same way `list` and `remove` do: `update Contoso.AgentSkills`, `update @acme/skills`, `update anthropics/skills`. `update Contoso.AgentSkills@2.1.0` moves to exactly that version.
- **Reinstalls keep the original layout**: same scope, same agents, and symlinked installs stay symlinked. Before, every update reinstalled in copy mode for whichever agents were detected.
- New `update` options: `-a/--agent`, `--force` (reinstall even without a detected change), `--prerelease`, `--version-lock none|major|minor` (keep NuGet/npm updates on the installed major or major.minor version), `--json`, `--exit-code` (exit 1 when updates are available, 2 when a check failed), `--nuget-source`, `--npm-registry`.
- **`outdated`** (alias `check`): the read-only report. `check` used to be an alias of `update` and could install updates.
- **`restore`**: installs every skill in `./skills-lock.json` at its locked version or ref, for fresh clones and CI. Skips skills already present (`--force` reinstalls), and `--frozen` fails when a source's content no longer matches the lock.
- **`--version`** prints the tool version; **`version`** also shows the commit, runtime, OS, and lock file paths (`--json` available).
- `list --json` and `list -p/--project`; `remove -p/--project`.
- **Name conflicts are detected and can be resolved with an alias.** When `add` would install a skill whose name is already used in that scope by a skill from a different source (or by an untracked skill), it now stops before installing anything. Interactively it asks whether to install under another name (with a suggested name), replace, or skip. With `-y` or in CI it lists the conflicts with the commands that resolve them and exits `2`. Before, the existing skill was silently overwritten. `find` asks the same question.
- **`add --as <skill>=<new-name>`** installs a skill under another name; `add --replace` overwrites without asking. The installed `SKILL.md` gets the new `name` (the spec requires it to match the folder); the lock records `originalName`, so `update`, `restore`, and later `add`s of the same source keep the alias. `list` shows aliases.
- Lock entries keep fields they don't recognize when rewritten, so additions by other tools aren't lost.
- **NuGet packages: `skills/` at the package root is now the recommended layout**, the same one NuGet's `dotnet-package-skills` reads, so one package works with both tools. The older `contentFiles/any/any/skills/` layout is still read; when a package has both, `skills/` wins. The authoring guide and `samples/sample-nuget-package` use the new layout, and a new docs page compares AgentSkills CLI with `dotnet-package-skills`.

### Bug fixes

- **Project installs no longer write to the global lock.** Each scope now writes only its own lock file. Before, a project install also added a global lock entry, so `update -g` could reinstall project skills globally. `update` reports the leftover entries as stale and removes them; `outdated` only lists them.
- **`update` never checked project-scope skills.** Every project skill was reported as "Missing folder hash". Project skills are now compared against the hash in `skills-lock.json`.
- **`remove` and `list` respect scope** when matching by package: removing a project package no longer touches a same-named global skill or its global lock entry, and `list` shows each row's source from its own scope's lock.
- **`remove -a <agent>` no longer breaks other agents.** It used to delete the shared canonical copy and the lock entry even when other agents still used the skill (through a symlink).
- **Corrupt lock files are no longer silently discarded.** An unparseable lock is backed up to `<lock>.corrupt-<timestamp>` with a warning. A lock written in a newer format is never overwritten, and lock writes are atomic.
- Unknown `--agent` names produce a one-line error instead of a stack trace.
- `remove` lists every agent a skill was removed from. When several agents share `.agents/skills`, only the first one used to be named.
- Docs said `--copy` was the default install mode; the default has always been symlink.
- The git tree SHA recorded for a skill at the repository root is now the root tree SHA, which matches what the GitHub Trees API reports.

### Build and platform

- **Builds were failing with `NU1902`** (`Microsoft.Build.Tasks.Git` 8.0.0, GHSA-23fw-v26w-5fgq) because warnings are errors. The explicit `Microsoft.SourceLink.GitHub` reference is removed; SourceLink is built into the .NET 8+ SDK, so packages keep their source links.
- **.NET 11**: the tool already runs on .NET 11 and later through `RollForward=LatestMajor`; a new CI job tests the `net10.0` build on the .NET 11 runtime. No `net11.0` build is added (.NET 11 is an STS release).
- .NET 8 stays supported: the package keeps its `net8.0` build alongside `net10.0`.
- New `ci` workflow: build and test on Linux, Windows, and macOS for every push and pull request.

## 1.0.0 - 2026-06-03

First stable release. The CLI, source resolvers, lock format, and install layout are considered stable; future changes follow SemVer with no further breaking changes expected before 2.0.0. No functional changes since 0.2.2.

## 0.2.2 - 2026-05-26

Patch release. Interactive-prompt UX fix.

### Bug fixes

- **`add` no longer silently installs every skill when you hit Enter on an empty selection.** The skill picker treated "nothing checked" as "install all," which made it impossible to back out of the prompt without Ctrl+C (and Esc isn't bound by Spectre.Console). Empty selection now exits cleanly with "Nothing selected, skipping." Same fix applied to the agent picker. All three multi-select prompts (`add` skills, `add` agents, `remove`) now show "Ctrl+C to cancel" in the instructions.

## 0.2.1 - 2026-05-26

Patch release. Bug fixes + housekeeping; no new features.

### Bug fixes

- **`-v` / `--verbose` / `-q` / `--quiet` / `--trace` flags now actually work.** The parser was reading them for the logger pipeline but not removing them from the args array before Spectre.Console.Cli's parser saw them, so the global flags errored out with "Unexpected option ...". Strip the flags from args after parsing so Spectre only sees the rest.
- **NuGet install errors now name *which* feed failed and *why*.** Previously: "Failed to download NuGet package 'X' from any configured source" - users had to dig through `-v` output to see per-source failures (and `-v` was broken, see above). Now the per-source reasons are embedded directly in the visible error message - 401 / 404 / network errors / etc. surface immediately.

### Housekeeping (post-0.2.0)

- `LICENSE`: dropped the "Portions Copyright (c) Vercel Labs..." line. AgentSkills CLI is a clean-room C# port with no source code copied, so MIT's notice clause isn't triggered. Attribution now lives in `NOTICE` where it belongs.
- `NOTICE`: rewritten to use the current product name + a proper attribution block. Names what's shared with upstream (behavior, design, lock format) and what isn't (source code).
- README badges: NuGet + GitHub release badges now track stable releases only (not prereleases) and use shields.io default colors instead of the custom indigo.
- Repo-level `NuGet.config` added at root with `<clear />` + nuget.org only. Insulates the build from whatever feeds individual contributors have configured at the user level (fixes NU1507 build break under Central Package Management when extra feeds are registered globally).

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
