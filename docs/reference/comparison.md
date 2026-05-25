# AgentSkills vs `npx skills`

AgentSkills is a faithful .NET-native port of [`vercel-labs/skills`](https://github.com/vercel-labs/skills) that adds first-class NuGet and npm support plus a layer of ergonomics. This page is the full feature-by-feature table; the [FAQ entry](/faq#how-is-this-different-from-npx-skills) summarizes the highlights in prose.

## Capability matrix

| Capability | AgentSkills | `npx skills` |
|---|---|---|
| **NuGet packages** as a first-class source (public + private feeds, NuGet.config + credential providers) | yes | no NuGet path |
| **npm registry fetch** as a first-class source (public + private, `.npmrc` + scoped registries + `_authToken`) | yes | only `experimental_sync` from pre-installed `node_modules` |
| **Skills can ship inside existing library packages** (drop `skills/` into your `.nupkg` or `.tgz` - same package id, version-locked with the SDK, discoverable via standard package search) | yes | n/a - no NuGet path; npm only via experimental sync |
| **Version-aware target matching** - `remove Pkg@1.5.0` strict-matches; a "version 1.4.0 is installed" hint when the pin is wrong | yes | no concept of pinned matching |
| **`--path` works across every source** (NuGet, npm, git, local) - overrides where discovery scans inside the staged source | yes | only `/tree/<ref>/<path>` in GitHub URLs |
| **Unified positional targets** on `list` / `remove` - each arg matches as a skill name *or* any source format, union semantics | yes | separate positionals/flags |
| **`--by package\|path\|agent\|scope`** grouping on `list` | yes | flat output |
| **Install paths surfaced everywhere** - `Path` column on `add` output, `Installed under ...` summary, `--paths` on `list` | yes | success status without paths |
| **Multi-targeted runtime** - LTS .NET 8 *and* latest .NET 10 from one .nupkg | yes | Node 18+ (single ecosystem) |
| **DI extension points** - register `ISkillSourceFactory` for new source types (cargo, oci, conda, ...) and `ISkillSearchProvider` for new search backends (internal registries, GitHub topic search, ...) with no core changes | yes | procedural, no public extension contract |
| **Typed exception hierarchy** with carried `ExitCode` - clean single-line errors, no stack-trace spam | yes | ad-hoc throws |
| **Verbosity control** - `-v` / `-q` / `--trace` route through `Microsoft.Extensions.Logging` to a Spectre logger | yes | console writes only |
| **0 build warnings**, `TreatWarningsAsErrors`, NetAnalyzers + Meziantou.Analyzer at Recommended | yes | not enforced |

## Where the two tools are identical

By design - the two tools interoperate, so being identical here is a feature, not a gap:

- **`SKILL.md` format** - same YAML frontmatter schema (`name`, `description`, optional `metadata`, optional `internal` flag)
- **Lock file shapes** - both use `~/.agents/.skill-lock.json` (v3) for global state and `./skills-lock.json` (v1) for project state, so installs done by either tool are visible to the other
- **Universal `.agents/skills/` install directory** - shared by every agent that follows the universal convention
- **Well-known endpoint** - both support `/.well-known/agent-skills/index.json` (modern) and `/.well-known/skills/index.json` (legacy fallback)
- **GitHub auth chain** - both fall back through `GITHUB_TOKEN` -> `GH_TOKEN` -> `gh auth token` after an unauthenticated 403

## What AgentSkills v1 deliberately doesn't ship

| Feature | Why it's not in AgentSkills |
|---|---|
| **Telemetry** | Privacy by default. Lock files give you everything you need to track usage internally. |
| **`experimental_install`** | Still experimental in `npx skills`; holding for stability. |
| **`experimental_sync`** | Same. AgentSkills' npm fetch covers the actual use case (install from registry). |
| **The full 55-agent registry** | v1 ships the most-used 5: `claude-code`, `codex`, `cursor`, `opencode`, `universal`. Adding more is mechanical - [open an issue](https://github.com/mysticmind/agentskills-cli/issues) for any specific agent you need. |
| **Live fzf-style `find` UI** | The current table-output `find` is responsive enough; a TUI variant may land in a later version if there's demand. |

## Coexistence

If you have both tools installed, they coexist by design - different binaries (`skills` vs `agentskills-cli`), shared lock and install dirs. See [Troubleshooting -> coexistence](/troubleshooting#agentskills-cli-and-npx-skills-coexistence) for the details.

## Next

- [FAQ](/faq) - common questions with concise answers
- [Source formats](/sources/) - every input type AgentSkills accepts
- [Search providers (extension point)](/reference/search-providers) - the contract for custom search backends
