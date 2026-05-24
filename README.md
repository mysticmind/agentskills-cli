# AgentSkills

`dnx agentskills` - install agent skills from **GitHub**, **NuGet**, **npm**, well-known endpoints, or local folders into Claude Code, Cursor, Codex, OpenCode, and friends. A .NET port of [vercel-labs/skills](https://github.com/vercel-labs/skills) following the open [Agent Skills spec](https://agentskills.io).

A *skill* is a folder containing a `SKILL.md` (YAML frontmatter + markdown body) plus optional supporting files. `AgentSkills` installs those folders into the right place for whichever coding agent you use (Claude Code, Cursor, Codex, OpenCode, …) so the agent can read and apply them.

Both the `SKILL.md` format and the well-known discovery endpoint follow the open **[Agent Skills specification](https://agentskills.io)**, so skills published for the upstream npm tool, the broader ecosystem, or any other spec-compliant client work here too.

This port keeps every source the upstream npm tool supports (local folders, GitHub, GitLab, any git URL, well-known endpoints) and adds two more:

- **NuGet packages** - public *and* private feeds, using your existing `NuGet.Config` and credential providers.
- **npm packages** - public *and* private registries, using your existing `~/.npmrc` and project `.npmrc` (scoped registries and `_authToken`/`_auth` honored).

## What this port does beyond upstream

A few places where AgentSkills goes further than `npx skills` today:

| Capability | AgentSkills | Upstream |
|---|---|---|
| **NuGet packages** as a first-class source (public + private feeds, NuGet.config + credential providers) | yes | no NuGet path |
| **npm registry fetch** as a first-class source (public + private, `.npmrc` + scoped registries + `_authToken`) | yes | only `experimental_sync` from pre-installed `node_modules` |
| **Version-aware target matching** - `remove Pkg@1.5.0` strict-matches; a "version 1.4.0 is installed" hint when the pin is wrong | yes | no concept of pinned matching |
| **`--path` works across every source** (NuGet, npm, git, local) - overrides where discovery scans inside the staged source | yes | only `/tree/<ref>/<path>` in GitHub URLs |
| **Unified positional targets** on `list` / `remove` - each arg matches as a skill name *or* any source format, union semantics | yes | separate positionals/flags |
| **`--by package\|path\|agent\|scope`** grouping on `list` | yes | flat output |
| **Install paths surfaced everywhere** - `Path` column on `add` output, `Installed under …` summary, `--paths` on `list` | yes | success status without paths |
| **Multi-targeted runtime** - LTS .NET 8 *and* latest .NET 10 from one .nupkg | yes | Node 18+ (single ecosystem) |
| **DI extension points** - register `ISkillSourceFactory` for new source types (cargo, oci, conda, ...) and `ISkillSearchProvider` for new search backends (internal registries, GitHub topic search, ...) with no core changes | yes | procedural, no public extension contract |
| **Typed exception hierarchy** with carried `ExitCode` - clean single-line errors, no stack-trace spam | yes | ad-hoc throws |
| **Verbosity control** - `-v` / `-q` / `--trace` route through `Microsoft.Extensions.Logging` to a Spectre logger | yes | console writes only |
| **0 build warnings**, `TreatWarningsAsErrors`, NetAnalyzers + Meziantou.Analyzer at Recommended | yes | not enforced |

**Not in v1.** None of these are blockers for daily use; the working set above covers the install / discover / update flow end-to-end.

- *Maybe later, on demand*: additional agents from the upstream's 55-agent registry, live fzf-style `find` UI.
- *Deliberately not shipped*: telemetry (privacy by default), `experimental_install` / `experimental_sync` (upstream-experimental, holding for stability).

---

## Table of contents

- [What this port does beyond upstream](#what-this-port-does-beyond-upstream)
- [Install](#install)
- [Concepts](#concepts)
- [Commands](#commands)
  - [`add`](#add)
  - [`list`](#list)
  - [`remove`](#remove)
  - [`init`](#init)
  - [`find`](#find)
  - [`update`](#update)
- [Source formats](#source-formats)
- [Authoring skills](#authoring-skills)
- [Publishing skills as a NuGet package](#publishing-skills-as-a-nuget-package)
  - [Publishing skills as an npm package](#publishing-skills-as-an-npm-package)
- [Agents](#agents)
- [Where files land](#where-files-land)
- [Search providers (extension point)](#search-providers-extension-point)
- [Lock files](#lock-files)
- [Environment variables](#environment-variables)
- [Common workflows](#common-workflows)
- [Building from source](#building-from-source)
- [Troubleshooting](#troubleshooting)
- [Related](#related)
- [License](#license)

---

## Install

### Option A - one-shot via `dnx` (.NET 10+)

No install step required. `dnx` downloads the tool from NuGet on first use and caches it.

```bash
dnx agentskills --help
dnx agentskills add ./my-skill -a claude-code
```

### Option B - global tool

```bash
dotnet tool install --global agentskills
agentskills --help
```

Update or uninstall:

```bash
dotnet tool update --global agentskills
dotnet tool uninstall --global agentskills
```

### Requirements

- **.NET 8 LTS** or **.NET 10** runtime (the tool is multi-targeted; `dotnet tool install` picks the right build for whichever runtime you have).
- **`dnx agentskills` requires .NET 10** specifically - `dnx` itself ships only with the .NET 10 SDK. On .NET 8 use `dotnet tool install --global agentskills` and call `agentskills` directly.
- `git` on `PATH` (only when installing from git URLs / GitHub / GitLab).
- Building from source requires the **.NET 10 SDK** (it can build both TFM outputs; the .NET 8 SDK cannot build the net10 output).

---

## Concepts

**Skill** - a folder with a `SKILL.md` at the root:

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

**Skill collection** - a source can carry one skill or many. Each child folder with a `SKILL.md` is its own skill:

```
my-skills-repo/                    # a single source (git repo, .nupkg, .tgz, …)
└── skills/
    ├── skill-one/SKILL.md         # installed independently
    ├── skill-two/SKILL.md
    └── skill-three/
        ├── SKILL.md
        └── reference.md
```

`agentskills add <source>` discovers every skill in the source and (by default) installs all of them. Narrow the set with `-s <name>` for a specific skill, `-s '*'` to be explicit about "all", or drop the flag and pick interactively with the multi-select prompt. Use `--path <subdir>` when the skills live somewhere non-standard inside the source.

**Source** - where a skill collection comes from: a local folder, a git repo, a NuGet package, an npm package, or an HTTPS endpoint that follows the well-known discovery convention.

**Agent** - a target tool (Claude Code, Cursor, Codex, OpenCode, …). Each agent reads skills from a specific directory. `AgentSkills` knows where each agent looks and copies the skill there.

**Scope** - *project* installs land in the current directory (`./.agents/skills/…`); *global* installs (`-g`) land in your home (`~/.agents/skills/…`).

**Universal vs per-agent agents** - Cursor, Codex, OpenCode and the "universal" target share a single canonical `.agents/skills` directory that they all read directly. Claude Code has its own `.claude/skills` directory; on supported filesystems we symlink it to the canonical copy, otherwise we copy.

---

## Commands

Every command supports `--help`:

```bash
agentskills --help
agentskills add --help
```

### `add`

Install one or more skills from a source.

```
agentskills add <source> [-g] [-a agent...] [-s skill...] [-y] [--copy|--symlink] [--nuget-source URL] [--npm-registry URL]
```

| Flag | Meaning |
|---|---|
| `<source>` | Local path, GitHub shorthand, URL, or NuGet package id (see [Source formats](#source-formats)). |
| `-g`, `--global` | Install to the user-wide directory (`~/.agents/skills/…`) instead of the project. |
| `-a`, `--agent <NAME>` | Target a specific agent. Pass multiple times for multiple agents. Omit to auto-detect installed agents and (interactively) prompt. |
| `-s`, `--skill <NAME>` | Install specific skill(s) by name from the source. Use `'*'` to mean "all". Omit to install everything (with a multi-select prompt unless `-y`). |
| `-y`, `--yes` | Non-interactive. Skip all prompts and accept defaults: install all discovered skills, target all detected agents. Required for scripts/CI. |
| `--copy` | Materialize files into each agent's directory (default - cross-platform safe). |
| `--symlink` | Symlink each agent's directory to the canonical copy. Falls back to copy on filesystems that don't support it (Windows without dev mode, restricted volumes). |
| `--nuget-source <URL>` | One-shot override for the NuGet feed. By default `NuGet.Protocol` uses every enabled feed in your `NuGet.Config`. |
| `--npm-registry <URL>` | One-shot override for the default npm registry. By default `~/.npmrc` + project `.npmrc` + per-scope rules apply. |
| `--path <PATH>` | Restrict discovery to a subdirectory of the source. Overrides any subpath the source string carried. Use when a package puts its skills somewhere non-standard (e.g. `ai/prompts/` instead of `skills/`). Rejected if the path contains `..` or doesn't exist inside the staged source. |

**Examples:**

```bash
# Install one specific skill from a repo for Claude Code, non-interactive
agentskills add vercel-labs/agent-skills -a claude-code -s web-design-guidelines -y

# Install everything in a NuGet package into the project, all detected agents
agentskills add MyOrg.AgentSkills -y

# Install all skills from a private NuGet feed at a pinned version, globally
agentskills add MyOrg.AgentSkills@1.2.3 -g -y \
  --nuget-source https://pkgs.contoso.com/v3/index.json

# Install from a GitHub branch with a subpath
agentskills add https://github.com/vercel-labs/agent-skills/tree/main/skills/web-design-guidelines

# Install a local skill into the current project for Cursor
agentskills add ./my-local-skill -a cursor

# Source uses a non-conventional layout - point at it explicitly
agentskills add MyOrg.AgentSkills --path ai/prompts -y
agentskills add @my-org/agent-skills --path src/skills -y
agentskills add anthropics/skills --path docs/skills -y
```

**Discovery, briefly.** Without `--path`, skills are found via three layered passes: (1) source-type conventions (`contentFiles/any/any/skills/` for NuGet, `package/skills/` then `package/contentFiles/...` for npm, the upstream priority dir list for git), then (2) recursive scan up to 5 levels deep, skipping `node_modules`, `.git`, `dist`, `build`, `__pycache__`. With `--path`, the scan is restricted to that one subdirectory of the staged source.

**Output.** After a successful install, the result table includes a `Path` column with the exact file location for every `(skill, agent)` pair, followed by an `Installed under …` summary listing the distinct directory roots:

```
╭─────────────┬─────────────┬────────┬─────────────────────────────────────────╮
│ Skill       │ Agent       │ Result │ Path                                    │
├─────────────┼─────────────┼────────┼─────────────────────────────────────────┤
│ hello-skill │ Universal   │ copied │ /path/to/project/.agents/skills/hello-… │
│ hello-skill │ Claude Code │ copied │ /path/to/project/.claude/skills/hello-… │
╰─────────────┴─────────────┴────────┴─────────────────────────────────────────╯
Installed under /path/to/project/.agents/skills, /path/to/project/.claude/skills
Done.
```

After the fact you can recover the same paths any time with `agentskills list --paths` (see below) or by reading the rules in [Where files land](#where-files-land).

### `list`

Inspect installed skills.

```
agentskills list [<target>...] [-g] [-a agent...] [--by package|path|agent|scope] [--paths]
```

| Argument | Meaning |
|---|---|
| `<target>` | Optional filter. Each argument is matched first as a skill name, then as a source. "Source" accepts **any** shape `add` accepts: local paths, GitHub `owner/repo` shorthand or URL (with or without `/tree/<ref>/<path>`), GitLab URLs, NuGet package id, npm package id (`@scope/name`, bare, or `npm:`-prefixed), arbitrary git URL. Version suffix ignored. Repeatable; the match is a union. Omit to list everything. |

| Flag | Meaning |
|---|---|
| `-g`, `--global` | Show only globally installed skills. Default shows both project and global. |
| `-a`, `--agent <NAME>` | Limit the view to specific agents. |
| `--by <KEY>` | Group the output. Values: `package`, `path`, `agent`, `scope`. Default is a single flat table. |
| `--paths` | Add a column with each skill's on-disk install path. |

Default output is a flat Spectre table: skill name, scope (`project` / `global`), the agents that have it installed, the package source (from the lock file, with the source-type tag), and the description.

**Examples:**

```bash
# Filter by skill name
agentskills list hello-skill

# Filter by source - same parsing as `agentskills add`
agentskills list @acme/sample-skills                          # npm scoped
agentskills list npm:sample-pkg                                 # npm unscoped
agentskills list nuget:MyOrg.AgentSkills                      # NuGet (explicit; the bare 'MyOrg.AgentSkills' also works)
agentskills list anthropics/skills                            # GitHub shorthand
agentskills list https://github.com/anthropics/skills         # GitHub URL - same lock entries
agentskills list https://gitlab.com/group/sub/repo            # GitLab
agentskills list /abs/path/to/local/skill                     # local path

# Mix skill names and sources - the union is shown
agentskills list hello-skill anthropics/skills MyOrg.AgentSkills

# Group by package (one mini-table per source)
agentskills list --by package

# Group by install directory + show the full path on each row
agentskills list --by path --paths

# Group by agent or scope
agentskills list --by agent
agentskills list --by scope
```

Skills not tracked in any lock (installed manually, or before lock tracking) appear under `(untracked)` when `--by package` is set, and with a `-` in the Source column otherwise.

**Versions** - for NuGet and npm targets, dropping the `@version` matches any installed version; pinning a version (`@acme/sample-skills@1.5.0`) requires an exact match. If the requested version isn't installed but a different version is, the command prints a hint:

```
$ agentskills list npm:@acme/sample-skills@1.5.0
No installed skills matched npm:@acme/sample-skills@1.5.0.
hint: @acme/sample-skills is installed at version(s) 1.4.0; drop the @version to list anyway.
```

GitHub refs (`anthropics/skills#main`) are not version constraints - the lock tracks them separately, so they're ignored when matching.

### `remove`

Remove installed skills.

```
agentskills remove [<target>...] [-g] [-a agent...] [-y]
```

| Argument | Meaning |
|---|---|
| `<target>` | Skills to remove. Each argument is matched first as a skill name, then as a source - **any** shape `add` accepts works (GitHub `owner/repo` or URL, GitLab URL, NuGet package id, npm package id, local path). Repeatable; the match is a union. Omit to be prompted with a multi-select of everything installed. |

| Flag | Meaning |
|---|---|
| `-g`, `--global` | Remove from the global scope. Default removes from both scopes. |
| `-a`, `--agent <NAME>` | Limit removal to specific agents. By default all agents that have the skill get cleaned. |
| `-y`, `--yes` | Skip the "Remove N skill(s)?" confirmation. |

After removal, the canonical `.agents/skills/<name>` directory is also cleaned up, and lock entries are dropped.

**Examples:**

```bash
# Remove by skill name
agentskills remove hello-skill -y

# Remove every skill installed from a package - same parsing as `agentskills add`
agentskills remove nuget:MyOrg.AgentSkills -y            # NuGet (bare 'MyOrg.AgentSkills' also works)
agentskills remove @acme/sample-skills -y                # npm
agentskills remove anthropics/skills -y                  # GitHub
agentskills remove https://gitlab.com/group/repo -y      # GitLab

# Mix skill names and sources, narrow to one agent
agentskills remove some-extra-skill MyOrg.AgentSkills -a claude-code -y
```

**Versions** behave the same as in `list`: a bare package id (`MyOrg.AgentSkills`) matches every installed version - useful for upgrades where you don't remember which version is live. Pinning a version (`MyOrg.AgentSkills@1.2.3`) strict-matches, and if nothing matches the command prints a hint with the installed version(s). Scoped npm names are handled correctly: in `@acme/sample-skills@1.0.0` the leading `@` is the scope marker, only the trailing `@1.0.0` is the version.

### `init`

Scaffold a new `SKILL.md` template in a directory.

```
agentskills init [PATH] [-y]
```

| Flag | Meaning |
|---|---|
| `PATH` | Directory to scaffold in. Defaults to the current directory. Created if missing. |
| `-y`, `--yes` | Overwrite an existing `SKILL.md` without asking. |

The skill's `name` defaults to a kebab-case derivation of the directory name.

### `find`

Search registered providers for skills. Out of the box that's [skills.sh](https://skills.sh); add more by registering an `ISkillSearchProvider` (see [Search providers](#search-providers-extension-point)).

```
agentskills find [QUERY] [-g] [-y] [--provider NAME...] [--list-providers]
```

| Flag | Meaning |
|---|---|
| `QUERY` | Search terms. Omit in an interactive shell to be prompted. |
| `-g`, `--global` | When picking a result, install it globally. |
| `-y`, `--yes` | Non-interactive output only: print the result table and exit (no install prompt). |
| `--provider <NAME>` | Restrict the fan-out to specific providers. Repeatable; the search runs against the union. Unknown name errors with the available-providers list. Omit to query every enabled provider. |
| `--list-providers` | Print the registered providers (name, enabled/disabled, implementing type) and exit. |

By default `find` **fans out across every enabled provider in parallel**, applies a 10-second timeout to each, dedupes results by `(source, skill name)`, and sorts by installs. A failing or slow provider is dropped from that run - the rest still render. Each result row is tagged with its origin in a `Provider` column.

In an interactive shell, results render as a table and then a `SelectionPrompt` lets you pick one to install via the regular `add` pipeline.

**Examples:**

```bash
# All enabled providers (default)
agentskills find react

# One specific provider
agentskills find react --provider skills.sh

# Union of two named providers
agentskills find react --provider skills.sh --provider contoso

# What's wired up?
agentskills find --list-providers
```

### `update`

Detect upstream changes for tracked GitHub skills and reinstall them.

```
agentskills update [<name>...] [-g] [-p] [--check] [-y]
```

| Flag | Meaning |
|---|---|
| `<name>` | Limit the check to specific skill names. Omit to check everything tracked. |
| `-g`, `--global` | Only the global scope. |
| `-p`, `--project` | Only the project scope. |
| Both / neither | Both scopes (auto-detected based on the presence of `skills-lock.json` or `.agents/skills/`). |
| `--check` | Report drift only - do **not** install. Exit code is always 0; use the table output to decide. |
| `-y`, `--yes` | Skip the install confirmation. |

**How it works:** `add` records the GitHub tree SHA (`skillFolderHash`) and skill folder path (`skillPath`) for every skill installed from a git source. `update` groups the lock by `owner/repo`, calls the GitHub Trees API once per repo, looks up each skill's current tree SHA, and reinstalls any that have drifted.

**GitHub auth** is lazy and mirrors upstream:

1. Try unauthenticated (sufficient for most personal use - 60 req/h per IP).
2. On a rate-limit 403, try `GITHUB_TOKEN`.
3. Then `GH_TOKEN`.
4. Then `gh auth token` (prints a one-time stderr note so you know).

Sources that can't be checked automatically (local paths, generic git URLs, GitLab, NuGet, well-known endpoints, or skills installed before tree-SHA tracking) appear in a separate **Skipped** table with an explanation.

---

## Source formats

Detection is order-sensitive - the first rule that matches wins. This list mirrors upstream's `source-parser.ts` plus a new NuGet branch.

### Local

```bash
agentskills add .                     # current directory
agentskills add ./my-skill
agentskills add ../shared/skill
agentskills add /abs/path/to/skill
agentskills add C:\skills\my-skill    # Windows
```

### NuGet

```bash
agentskills add MyOrg.AgentSkills              # latest stable from configured feeds
agentskills add MyOrg.AgentSkills@1.2.3        # specific version
agentskills add nuget:MyOrg.AgentSkills@1.2.3  # explicit prefix (forces NuGet)
```

`AgentSkills` uses `NuGet.Protocol` with `Settings.LoadDefaultSettings()`, so every feed listed in your machine / user / per-project `NuGet.Config` is searched in order. Credential providers (Azure Artifacts, GitHub Packages, etc.) are honored automatically - no flag needed.

Override the feed list for a single command with `--nuget-source <URL>`.

### npm

```bash
agentskills add @my-org/agent-skills                # scoped - auto-detected as npm
agentskills add @my-org/agent-skills@1.2.3          # pinned version
agentskills add @my-org/agent-skills@next           # dist-tag
agentskills add npm:sample-pkg                        # unscoped - requires npm: prefix
agentskills add npm:sample-pkg@1.3.0
```

> Why the prefix for unscoped? A bare `lodash.merge` matches the NuGet shorthand
> (this *is* a .NET tool), so unscoped npm names need `npm:` to disambiguate.
> Scoped names (`@scope/name`) start with `@` and are unambiguous.

Registry, auth, and scope routing are read from your `.npmrc` files in this order
(later wins): `~/.npmrc`, then the project's `./.npmrc`.

| `.npmrc` key | What it does |
|---|---|
| `registry=https://registry.npmjs.org/` | Default registry. |
| `@my-org:registry=https://npm.contoso.com/team/` | Use a different registry for one scope. |
| `//npm.contoso.com/team/:_authToken=…` | Bearer token sent to that registry/path. |
| `//npm.contoso.com/:_auth=base64(user:pass)` | Basic auth (legacy). |
| `${ENV_VAR}` anywhere in a value | Expanded from process env at load time. |

The token is matched against the registry URL by host + longest path prefix, so a
token configured under `//npm.contoso.com/team/` is used for the `team/` registry
but not for `//npm.contoso.com/other/`.

Override the default registry for a single command with `--npm-registry <URL>`.

**Package layout:** the tarball roots at `package/` per the npm spec. We look for
`package/skills/` first, then `package/contentFiles/any/any/skills/`, then fall
back to a recursive scan. Mirrors how a multi-skill GitHub repo or `.nupkg` works:

```
@my-org/agent-skills-1.2.3.tgz
└─ package/
   ├─ package.json
   └─ skills/
      ├─ skill-one/SKILL.md
      └─ skill-two/SKILL.md
```

### GitHub shorthand (`owner/repo`)

```bash
agentskills add vercel-labs/agent-skills
agentskills add vercel-labs/agent-skills/skills/web-design-guidelines   # subpath
agentskills add vercel-labs/agent-skills#main                            # ref
agentskills add vercel-labs/agent-skills@web-design-guidelines          # single-skill filter
agentskills add vercel-labs/agent-skills#main@web-design-guidelines     # both
```

### Full GitHub URL

```bash
agentskills add https://github.com/vercel-labs/agent-skills
agentskills add https://github.com/vercel-labs/agent-skills.git
agentskills add https://github.com/vercel-labs/agent-skills/tree/main/skills/web-design-guidelines
```

### GitLab

```bash
agentskills add gitlab:group/repo
agentskills add https://gitlab.com/group/repo
agentskills add https://gitlab.com/group/subgroup/repo                    # subgroups supported
agentskills add https://gitlab.com/group/repo/-/tree/main/skills/foo      # subpath + ref
```

### Arbitrary git URL

```bash
agentskills add git@github.com:vercel-labs/agent-skills.git
agentskills add https://git.example.com/team/skills.git
agentskills add ssh://git@git.example.com/team/skills.git
```

### Well-known endpoint (RFC 8615-style)

```bash
agentskills add https://skills.example.com
agentskills add https://skills.example.com/team
```

The endpoint must serve `/.well-known/agent-skills/index.json` (the modern path) or `/.well-known/skills/index.json` (legacy fallback). Both schemas are supported:

- **v0.2.0** ([spec](https://agentskills.io), [JSON schema](https://schemas.agentskills.io/discovery/0.2.0/schema.json)): index entries declare `$schema`, `type: skill-md|archive`, `url`, and mandatory `digest: sha256:…`. The digest is verified after download. Archives may be `.zip` or `.tar.gz` (caps: 50 MB unpacked, 1000 files, no symlinks).
- **v0.1.0 (legacy)**: index entries with `name`, `description`, `files: [...]`, where files are fetched individually from `<base>/<wellknown>/<name>/<file>`.

The schema URL acts as the version marker: an index with `"$schema": "https://schemas.agentskills.io/discovery/0.2.0/schema.json"` is parsed as v0.2.0, an index with no `$schema` is parsed as v0.1.0, and any other `$schema` value is rejected (so future schema bumps don't get silently misinterpreted).

---

## Authoring skills

A skill is just a folder with a `SKILL.md`. Run `agentskills init` to scaffold one.

`SKILL.md` schema:

```yaml
---
name: <slug>             # required, string. Used as the install directory name (kebab-case).
description: <one line>  # required, string. Shown in lists and to agents at discovery time.
metadata:                # optional, free-form object.
  internal: false        # if true, hidden unless INSTALL_INTERNAL_SKILLS=1 or the user names it explicitly.
  any: thing             # arbitrary fields passed through to the installer.
---
# Markdown body the agent reads.
```

**Multi-skill repos.** Place each skill in its own folder. By default the discovery scan prefers well-known subdirectories (`skills/`, `.agents/skills/`, `.claude/skills/`, …) before recursing. Up to 5 levels of recursion. `node_modules/`, `.git/`, `dist/`, `build/`, and `__pycache__/` are skipped.

**Excluded from copy** (mirrors upstream): `metadata.json`, `.git/`, `__pycache__/`, `__pypackages__/`. Broken symlinks are skipped without aborting the install.

---

## Publishing skills as a NuGet package

`AgentSkills` uses the standard NuGet `contentFiles` layout. A minimal package looks like:

```
my-skills.csproj
contentFiles/
└── any/
    └── any/
        └── skills/
            ├── skill-one/
            │   ├── SKILL.md
            │   └── reference.md
            └── skill-two/
                └── SKILL.md
```

A working `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoWarn>$(NoWarn);NU5128;NU5127</NoWarn>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <PackageId>MyOrg.AgentSkills</PackageId>
    <Version>1.0.0</Version>
    <Description>Our team's curated agent skills.</Description>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="contentFiles\any\any\skills\**\*">
      <Pack>true</Pack>
      <PackagePath>contentFiles\any\any\skills\</PackagePath>
      <BuildAction>None</BuildAction>
      <CopyToOutput>false</CopyToOutput>
    </Content>
  </ItemGroup>
</Project>
```

Build and push:

```bash
dotnet pack -o ./out
dotnet nuget push ./out/MyOrg.AgentSkills.1.0.0.nupkg \
  --source https://pkgs.contoso.com/v3/index.json \
  --api-key $API_KEY
```

A complete example lives in [`samples/sample-nuget-package`](samples/sample-nuget-package).

> If you don't follow the `contentFiles/any/any/skills/` convention, `AgentSkills` still falls back to a recursive scan inside the extracted `.nupkg`. The convention is just the fast path.

### Publishing skills as an npm package

For npm, the conventional layout is `package/skills/<name>/SKILL.md` (which is what `npm pack` produces from a top-level `skills/` directory):

```
@my-org/agent-skills/
├── package.json                              # scoped names auto-detect as npm
├── README.md
└── skills/
    ├── skill-one/SKILL.md
    └── skill-two/SKILL.md
```

Minimum `package.json`:

```json
{
  "name": "@my-org/agent-skills",
  "version": "0.1.0",
  "description": "Our team's curated agent skills.",
  "license": "MIT",
  "files": ["skills/", "README.md"],
  "keywords": ["agent-skills", "skills"]
}
```

Pack and publish (auth via your normal `~/.npmrc`):

```bash
npm pack                                                    # → @my-org-agent-skills-0.1.0.tgz
npm publish --access public                                 # public; drop --access for private/scoped
```

Users install with:

```bash
agentskills add @my-org/agent-skills -y                     # scoped → auto-detected as npm
agentskills add npm:unscoped-pkg -y                         # unscoped requires explicit npm: prefix
```

A complete example lives in [`samples/sample-npm-package`](samples/sample-npm-package). The README there also covers local Verdaccio-registry testing without publishing.

> Same fallback behavior as NuGet: if the package puts skills somewhere other than `package/skills/`, AgentSkills will scan recursively. You can also pass `--path <subdir>` to point discovery at a non-standard layout explicitly.

---

## Agents

v1 ships five agent targets:

| `-a` name | Display | Project dir | Global dir | Universal? |
|---|---|---|---|---|
| `claude-code` | Claude Code | `.claude/skills` | `$CLAUDE_CONFIG_DIR/skills` or `~/.claude/skills` | no |
| `codex` | Codex | `.agents/skills` | `$CODEX_HOME/skills` or `~/.codex/skills` | yes |
| `cursor` | Cursor | `.agents/skills` | `~/.cursor/skills` | yes |
| `opencode` | OpenCode | `.agents/skills` | `$XDG_CONFIG_HOME/opencode/skills` or `~/.config/opencode/skills` | yes |
| `universal` | Universal | `.agents/skills` | `$XDG_CONFIG_HOME/agents/skills` or `~/.config/agents/skills` | yes |

Universal agents share the canonical `.agents/skills` directory - installing for one of them is effectively installing for all of them.

If you don't pass `-a`, `AgentSkills` auto-detects agents installed on the system and (in interactive mode) prompts you to pick.

---

## Where files land

A skill folder is always copied (or symlinked, with `--symlink`) into one of two locations, derived deterministically from the `(scope, agent)` pair.

**Project scope** (default - no `-g`), with `$PROJECT` = current working directory:

| Agent | Install path |
|---|---|
| `claude-code` | `$PROJECT/.claude/skills/<skill-name>/` |
| `codex`, `cursor`, `opencode`, `universal` | `$PROJECT/.agents/skills/<skill-name>/` (shared canonical dir) |

**Global scope** (`-g`), with `$HOME` = user home (and the listed env vars taking precedence if set):

| Agent | Install path |
|---|---|
| `claude-code` | `$CLAUDE_CONFIG_DIR/skills/<skill-name>/` or `$HOME/.claude/skills/<skill-name>/` |
| `codex` | `$CODEX_HOME/skills/<skill-name>/` or `$HOME/.codex/skills/<skill-name>/` |
| `cursor` | `$HOME/.cursor/skills/<skill-name>/` |
| `opencode` | `$XDG_CONFIG_HOME/opencode/skills/<skill-name>/` or `$HOME/.config/opencode/skills/<skill-name>/` |
| `universal` | `$XDG_CONFIG_HOME/agents/skills/<skill-name>/` or `$HOME/.config/agents/skills/<skill-name>/` |

The `<skill-name>` is the kebab-case-sanitized form of the SKILL.md `name` field (lowercased, runs of non `[a-z0-9._]` collapsed to `-`, leading/trailing dots and hyphens stripped, capped at 255 chars).

**Three ways to see the paths for skills already on disk:**

1. `agentskills add …` prints them in the result table's `Path` column and in the `Installed under …` summary.
2. `agentskills list --paths` re-renders the same `Path` column for everything installed.
3. `agentskills list --by path --paths` groups skills by install directory - handy for a "what's actually in `~/.claude/skills/`?" view.

---

## Search providers (extension point)

`find` discovers skills through one or more **search providers**. AgentSkills ships one out of the box (`skills.sh`); you can register more without forking.

A provider implements `ISkillSearchProvider`:

```csharp
public interface ISkillSearchProvider
{
    string Name { get; }            // dedup + filter key, shown in the result table
    bool IsEnabled { get; }         // skip when false (auth missing, feature flag off, ...)
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct);
}
```

Then register it after `AddAgentSkillsRuntime`:

```csharp
services
    .AddAgentSkillsRuntime()
    .AddSingleton<ISkillSearchProvider, ContosoInternalSearchProvider>();
```

`find` calls every enabled provider in parallel (10-second timeout each), merges by `(source, name)`, sorts by installs, and tags each row with the provider it came from. A failing provider is dropped from that run; the rest still render.

**Conceptually**:

| Extension point | Role | Example implementations |
|---|---|---|
| `ISkillSourceFactory` | *where you install from* | `LocalSourceFactory`, `GitSourceFactory`, `NuGetSourceFactory`, `NpmSourceFactory`, `WellKnownSourceFactory` |
| `ISkillSearchProvider` | *where you discover from* | `SkillsShSearchProvider` (built-in), plus anything you register |

Use cases that justify a custom provider:

- **Internal corporate registry** - "find" results should include team-owned skills published behind your VPN.
- **GitHub topic search** - rank repos tagged `agent-skills` directly from the GitHub API.
- **Offline / installed-only** - cheap local provider that searches your lock files when you're disconnected.

---

## Lock files

`AgentSkills` writes two lock files so installs are reproducible and `update` has something to diff against.

### Global lock - `~/.agents/.skill-lock.json` (or `$XDG_STATE_HOME/skills/.skill-lock.json`)

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

- `sourceType` is one of `local`, `github`, `gitlab`, `git`, `nuget`, `well-known`.
- `skillFolderHash` is the git tree SHA of the skill's folder - captured via `git rev-parse HEAD:<path>` on the staged clone. Empty for non-git sources.
- `update` reads this file to know what to check.

### Project lock - `./skills-lock.json`

Schema v1, sorted alphabetically (for clean diffs), commit it to your repo:

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

- `computedHash` is a SHA-256 over the installed skill folder (relative path + bytes), independent of git.
- Written on every project install. Useful for CI to verify the on-disk state matches what's tracked.

---

## Environment variables

| Variable | Effect |
|---|---|
| `CLAUDE_CONFIG_DIR` | Override Claude home (default `~/.claude`). |
| `CODEX_HOME` | Override Codex home (default `~/.codex`). |
| `XDG_CONFIG_HOME` | XDG base used for OpenCode and Universal global dirs (default `~/.config`). |
| `XDG_STATE_HOME` | If set, the global lock lives at `$XDG_STATE_HOME/skills/.skill-lock.json` instead of `~/.agents/.skill-lock.json`. |
| `SKILLS_CLONE_TIMEOUT_MS` | Hard timeout for `git clone` in milliseconds (default `300000` = 5 min). |
| `INSTALL_INTERNAL_SKILLS` | Set to `1` or `true` to include skills with `metadata.internal: true` in scans. |
| `SKILLS_API_URL` | Override the search backend used by `find` (default `https://skills.sh`). |
| `GITHUB_TOKEN` / `GH_TOKEN` | Used by `update` only after a rate-limit 403. Silent. |

`git clone` is invoked with `GIT_TERMINAL_PROMPT=0` and `GIT_LFS_SKIP_SMUDGE=1` so it never blocks for a credential prompt or pulls LFS objects.

---

## Common workflows

### Bootstrap a fresh project with a curated skill set

```bash
cd my-app
agentskills add MyOrg.CuratedSkills -y
git add .agents/ skills-lock.json
git commit -m "chore: pin agent skills"
```

Teammates run `agentskills add <same source> -y` (or, once `install-from-lock` lands, just `skills install`) to reproduce.

### Add a single skill globally so it's available to every project

```bash
agentskills add vercel-labs/agent-skills -g -s web-design-guidelines -y
```

### Try out everything in a NuGet package, then prune

```bash
agentskills add MyOrg.AgentSkills -y                # installs all
agentskills list MyOrg.AgentSkills --paths          # what landed, with paths
agentskills remove unused-skill -y                  # drop one
agentskills remove MyOrg.AgentSkills -y             # …or roll the whole package back
```

### Drive `dnx` from CI without ever installing the tool

```bash
dnx agentskills -y -- add ./my-skill -a claude-code -y --copy
```

### Refresh everything from upstream

```bash
agentskills update --check                    # dry-run table
agentskills update -g -y                      # actually update globals
```

---

## Building from source

```bash
dotnet build
dotnet test                              # 41+ unit & integration tests
dotnet pack src/AgentSkills -o ./artifacts

# Try the freshly-packed tool without installing
dnx agentskills --source ./artifacts -y -- add ./samples/hello-skill -a universal -y --copy

# Or install it locally
dotnet tool install --global --add-source ./artifacts agentskills
agentskills --help
```

Project layout:

```
src/AgentSkills/
├── Commands/         # add, list, remove, init, find, update
├── Sources/          # local, git, NuGet, well-known, parser, GitHub API
├── Skills/           # SKILL.md parser, discovery, sanitizer, path safety
├── Agents/           # the 5-agent registry
├── Install/          # installer, copy/symlink, lock files
└── Ui/               # banner, prompts, spinners (Spectre.Console)

tests/AgentSkills.Tests/
├── SourceParserTests.cs
├── SkillCoreTests.cs
├── InstallerTests.cs
├── WellKnownSourceTests.cs   # spins a local HttpListener
└── GitHubApiTests.cs

samples/
├── hello-skill/              # minimal local skill
└── sample-nuget-package/     # demonstrates the contentFiles layout
```

---

## Troubleshooting

**`dnx: command not found`** - `dnx` ships with .NET 10 only. Either install the .NET 10 SDK, or use the global-tool path instead: `dotnet tool install --global agentskills && agentskills …` (works on .NET 8+).

**`The framework 'Microsoft.NETCore.App', version '10.0.0' was not found`** when invoking `agentskills` - your installed tool is the net10 build but only .NET 8 is present. Reinstall with `dotnet tool uninstall -g agentskills && dotnet tool install -g agentskills` and NuGet will pick the net8 build for you, or install the .NET 10 runtime side-by-side.

**`git: command not found`** during `agentskills add owner/repo` - install git and put it on `PATH`. Local and NuGet sources don't need git.

**`NuGet sources: No enabled NuGet sources found`** - your `NuGet.Config` lists no enabled feeds. Run `dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org` or pass `--nuget-source <URL>`.

**Private NuGet feed asks for credentials** - make sure the appropriate credential provider is installed for your feed (Azure Artifacts Credential Provider, GitHub Packages PAT in your `NuGet.Config`, etc.). `AgentSkills` doesn't add any new auth surface; if `dotnet restore` works against your feed, `agentskills add` will too.

**`update` shows "Could not fetch tree (rate-limited, private, or moved)"** - set `GITHUB_TOKEN` (or `GH_TOKEN`) and re-run. GitHub allows 60 unauthenticated requests per hour per IP.

**Symlinks failing on Windows** - pass `--copy` or enable Developer Mode. The installer falls back to copy automatically when symlink creation fails.

**Tests can't bind to a TCP port** - the well-known tests start a short-lived `HttpListener`. Re-run if a port races; the tests pick a free port each time.

**Want a shorter command than `agentskills`?** Alias it in your shell rc. The tool deliberately ships under `agentskills` (not `skills`) so it doesn't shadow `npx skills` when both are installed, but you can pick any short name you like locally:

```bash
# bash / zsh - add to ~/.bashrc, ~/.zshrc, etc.
alias as=agentskills

# fish - add to ~/.config/fish/config.fish
alias as agentskills
```

```powershell
# PowerShell - add to $PROFILE
Set-Alias -Name as -Value agentskills
```

After that: `as add ./my-skill -y`, `as list --by package`, etc.

**`agentskills`/`skills` confusion** - if you previously installed the upstream `npx skills` and now also have `agentskills` installed, the two coexist by design: separate binaries (`skills` vs `agentskills`), but they share the same lock file (`~/.agents/.skill-lock.json`) and install directory (`.agents/skills/`), so installs done by either tool are visible to both.

---

## Related

- **[Agent Skills specification](https://agentskills.io)** - the open spec for the `SKILL.md` format, well-known discovery endpoint, and v0.2.0 schema this CLI implements.
- **[`schemas.agentskills.io`](https://schemas.agentskills.io/)** - canonical JSON schemas (currently `discovery/0.2.0/schema.json`).
- **[vercel-labs/skills](https://github.com/vercel-labs/skills)** - the upstream npm CLI this project ports. Skills published for `npx skills` work with `dnx agentskills` and vice-versa.
- **[skills.sh](https://skills.sh)** - community directory powering `agentskills find`.

## License

MIT. Portions derived from [vercel-labs/skills](https://github.com/vercel-labs/skills) (MIT). See `LICENSE` and `NOTICE`.
