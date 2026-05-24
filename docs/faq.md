# FAQ

## How is this different from `npx skills`?

AgentSkills is a faithful .NET-native port of [`vercel-labs/skills`](https://github.com/vercel-labs/skills) (the `npx skills` CLI) with extras. The biggest deltas:

- **NuGet as a first-class source.** `npx skills` has no NuGet path. AgentSkills uses `NuGet.Protocol` and your existing `NuGet.config` + credential providers.
- **npm registry fetch (not just `node_modules` sync).** `npx skills` only offers `experimental_sync` against pre-installed packages. AgentSkills does full registry fetch with `.npmrc`-based auth.
- **Version-aware target matching** on `list` / `remove` for NuGet and npm.
- **`--path` works across every source**, not just GitHub URLs.
- **Multi-targeted runtime** - one `.nupkg` ships both .NET 8 and .NET 10 builds.
- **Extension points** - register `ISkillSourceFactory` for new source types, `ISkillSearchProvider` for new search backends.

Both tools share the same lock format and `.agents/skills/` directory by design, so you can switch between them without losing tracked state.

## Should I uninstall `npx skills` to use AgentSkills?

No. They coexist by design - different binaries (`skills` vs `agentskills`), shared lock and install dirs. See [troubleshooting](/troubleshooting#agentskills-and-npx-skills-coexistence).

## Why isn't my agent picking up an installed skill?

Three things to check:

1. **The skill is actually there**: `agentskills list --paths` shows the on-disk path. Confirm the file exists.
2. **The agent reads from that path**: see [Where files land](/reference/where-files-land). Some agents have their own config dir (`.claude/skills/`), others read from the universal `.agents/skills/`. AgentSkills places the skill in the right place per agent, but if the user invoked `add` with `-a universal` only and the agent doesn't read universal, no copy lands in the agent's specific dir.
3. **The agent has reloaded its skill index**: most agents read skills on startup. Restart the agent after a fresh install.

## Does the project lock work like `package-lock.json`?

Similar idea, different scope. The project lock (`./skills-lock.json`) records what skills are installed in the project, which source they came from, and a content hash. Commit it; teammates running `agentskills add` against the same source get reproducible installs.

It does **not** currently support `agentskills install` (restore-from-lock). That's a planned feature; for now, scripts can iterate the lock and re-run `add` for each entry.

## Can I host my own skill registry?

Yes, two ways:

1. **Well-known endpoint** - serve `/.well-known/agent-skills/index.json` at any HTTPS URL. See [Well-known endpoints](/sources/well-known) for the schema.
2. **Custom search provider** - register `ISkillSearchProvider` to make your registry queryable via `agentskills find`. See [Search providers](/reference/search-providers).

The two compose: a well-known endpoint handles install; a search provider handles discovery.

## Why no telemetry?

Deliberate choice. CLI tools that phone home to track usage are a common irritant; opt-out is often a fight users shouldn't have to win. AgentSkills makes the no-telemetry promise the default and won't change without a major version bump and a loud announcement.

If you want to measure how your team uses AgentSkills internally, the lock files (`~/.agents/.skill-lock.json` and `./skills-lock.json`) give you everything: what's installed, when, from where, by hash.

## Why is the package id lowercase (`agentskills`) and not `AgentSkills`?

Three identifiers, three audiences:

- **`PackageId` (nuget.org)**: `agentskills` - lowercase reads more naturally with `dnx agentskills` and `dotnet tool install --global agentskills`
- **Shell command**: `agentskills` - lowercase by Unix convention (`gh`, `docfx`, `dotnet-ef`)
- **C# namespace**: `AgentSkills` - PascalCase per .NET convention

The lowercase package id + command means the same string appears in install commands, shell invocations, and search results without case juggling.

## Is there a docs site for the spec itself?

[agentskills.io](https://agentskills.io) and [schemas.agentskills.io](https://schemas.agentskills.io/). AgentSkills implements the spec; the spec itself lives separately.
