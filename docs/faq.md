# FAQ

## How is this different from `npx skills`?

AgentSkills CLI is a faithful .NET-native port of [`vercel-labs/skills`](https://github.com/vercel-labs/skills) (the `npx skills` CLI) with extras. The biggest deltas:

- **NuGet as a first-class source.** `npx skills` has no NuGet path. AgentSkills CLI uses `NuGet.Protocol` and your existing `NuGet.config` + credential providers.
- **npm registry fetch (not just `node_modules` sync).** `npx skills` only offers `experimental_sync` against pre-installed packages. AgentSkills CLI does full registry fetch with `.npmrc`-based auth.
- **Version-aware target matching** on `list` / `remove` for NuGet and npm.
- **`--path` works across every source**, not just GitHub URLs.
- **Multi-targeted runtime** - one `.nupkg` ships both .NET 8 and .NET 10 builds.
- **Extension points** - register `ISkillSourceFactory` for new source types, `ISkillSearchProvider` for new search backends.

Both tools share the same lock format and `.agents/skills/` directory by design, so you can switch between them without losing tracked state.

## Should I uninstall `npx skills` to use AgentSkills CLI?

No. They coexist by design - different binaries (`skills` vs `agentskills-cli`), shared lock and install dirs. See [troubleshooting](/troubleshooting#agentskills-cli-and-npx-skills-coexistence).

## Why isn't my agent picking up an installed skill?

Three things to check:

1. **The skill is actually there**: `agentskills-cli list --paths` shows the on-disk path. Confirm the file exists.
2. **The agent reads from that path**: see [Where files land](/reference/where-files-land). Some agents have their own config dir (`.claude/skills/`), others read from the universal `.agents/skills/`. AgentSkills CLI places the skill in the right place per agent, but if the user invoked `add` with `-a universal` only and the agent doesn't read universal, no copy lands in the agent's specific dir.
3. **The agent has reloaded its skill index**: most agents read skills on startup. Restart the agent after a fresh install.

## Does the project lock work like `package-lock.json`?

Similar idea, different scope. The project lock (`./skills-lock.json`) records what skills are installed in the project, which source they came from, and a content hash. Commit it; teammates running `agentskills-cli add` against the same source get reproducible installs.

It does **not** currently support `agentskills-cli install` (restore-from-lock). That's a planned feature; for now, scripts can iterate the lock and re-run `add` for each entry.

## Can I host my own skill registry?

Yes, two ways:

1. **Well-known endpoint** - serve `/.well-known/agent-skills/index.json` at any HTTPS URL. See [Well-known endpoints](/sources/well-known) for the schema.
2. **Custom search provider** - register `ISkillSearchProvider` to make your registry queryable via `agentskills-cli find`. See [Search providers](/reference/search-providers).

The two compose: a well-known endpoint handles install; a search provider handles discovery.

## Why no telemetry?

Deliberate choice. CLI tools that phone home to track usage are a common irritant; opt-out is often a fight users shouldn't have to win. AgentSkills CLI makes the no-telemetry promise the default and won't change without a major version bump and a loud announcement.

If you want to measure how your team uses AgentSkills CLI internally, the lock files (`~/.agents/.skill-lock.json` and `./skills-lock.json`) give you everything: what's installed, when, from where, by hash.

## Why is the package id `agentskills-cli` and not `agentskills` or `AgentSkills`?

The hyphenated `-cli` suffix does three jobs at once:

- **Tells nuget.org browsers what the package is.** "AgentSkills CLI" reads clearly as "the CLI for the [Agent Skills spec](https://agentskills.io)" - no clicking through to find out.
- **Avoids brand collision with [agentskills.io](https://agentskills.io)** (the spec site). The package is not the spec; the `-cli` suffix makes that explicit.
- **Matches the repo name** `mysticmind/agentskills-cli` and the docs URL `mysticmind.github.io/agentskills-cli/` exactly. One name across every identifier.

Three derived identifiers stay in sync:

- **Install:** `dotnet tool install -g agentskills-cli` (or `dnx agentskills-cli` for one-shot)
- **Daily command:** `agentskills-cli add ...`, `agentskills-cli list`, etc. - same name as install, no mental mapping
- **C# namespace:** `AgentSkills` (PascalCase) - this is the source-code-only identifier and follows .NET convention; users never type it

If `agentskills-cli` is too long to type often, alias it in your shell. The [install guide](/getting-started/install#want-a-shorter-command) shows examples for bash, zsh, fish, and PowerShell.

## Is there a docs site for the spec itself?

[agentskills.io](https://agentskills.io) and [schemas.agentskills.io](https://schemas.agentskills.io/). AgentSkills CLI implements the spec; the spec itself lives separately.
