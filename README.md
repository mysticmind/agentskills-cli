![AgentSkills CLI logo](https://raw.githubusercontent.com/mysticmind/agentskills-cli/main/docs/public/logo-readme.png)

# AgentSkills CLI

[![NuGet](https://img.shields.io/nuget/vpre/agentskills-cli?logo=nuget&label=nuget&color=4F46E5)](https://www.nuget.org/packages/agentskills-cli) [![GitHub release](https://img.shields.io/github/v/release/mysticmind/agentskills-cli?include_prereleases&logo=github&label=release&color=4F46E5)](https://github.com/mysticmind/agentskills-cli/releases)

**The .NET commandline tool for the open [Agent Skills](https://agentskills.io) ecosystem.**

Install skills from GitHub, NuGet, npm, well-known endpoints, or local folders into Claude Code, Cursor, Codex, OpenCode, and any spec-compliant agent.

First-class NuGet & npm · library-bundled skill packages · extension points by design.

[📖 Documentation](https://mysticmind.github.io/agentskills-cli/) · [Why AgentSkills CLI?](https://mysticmind.github.io/agentskills-cli/why) · [Quick start](https://mysticmind.github.io/agentskills-cli/getting-started/quick-start) · [vs vercel-labs/skills](https://mysticmind.github.io/agentskills-cli/reference/comparison)

---

> **Initial release (`0.2.0`).** Feedback welcome on the install path, on any feature you expected and didn't find, and on anything that reads as confusing in the docs - [open an issue](https://github.com/mysticmind/agentskills-cli/issues).

## Install

```bash
# Global tool (recommended for daily use; .NET 8 LTS or .NET 10)
dotnet tool install --global agentskills-cli
agentskills-cli --help

# Or one-shot via dnx (.NET 10+) for CI / no-install scenarios
dnx agentskills-cli -- --help
```

See the [install guide](https://mysticmind.github.io/agentskills-cli/getting-started/install) for `.NET 8` notes, [shell shortcuts](https://mysticmind.github.io/agentskills-cli/getting-started/install#shortcuts) for both paths, and verification.

## At a glance

Once installed globally (`dotnet tool install --global agentskills-cli`):

```bash
# Install from any source - one command, multiple ecosystems
agentskills-cli add anthropics/skills              # GitHub shorthand
agentskills-cli add Contoso.AgentSkills            # NuGet package
agentskills-cli add @my-org/agent-skills           # npm package
agentskills-cli add ./my-local-skill               # local folder

# Inspect what's installed, grouped however you want
agentskills-cli list --by package

# Search community skills
agentskills-cli find testing

# Remove an entire package's worth of skills
agentskills-cli remove @my-org/agent-skills -y
```

Or one-shot via `dnx` for CI / no-install scenarios:

```bash
dnx agentskills-cli -- add ./my-skill
```

The `--` separates `dnx`'s own flags from the args passed through to the tool. For interactive daily use, prefer the installed-tool path - it's much less typing.

> **Tip:** Both paths support a one-character shell alias (`as add ./skill` instead of typing the full command). See [Shortcuts](https://mysticmind.github.io/agentskills-cli/getting-started/install#shortcuts) for bash / zsh / fish / PowerShell.

[Five-minute quick start →](https://mysticmind.github.io/agentskills-cli/getting-started/quick-start)

## What makes it different

- **NuGet as a first-class source** - public *and* private feeds via your existing `NuGet.config` and credential providers. No new auth surface.
- **npm registry fetch** (not just `node_modules` sync) - public *and* private registries via your existing `.npmrc`, scoped registries, `_authToken`.
- **Skills can ship inside library packages** - drop a `skills/` folder into your existing `.nupkg` or `.tgz`. Users get the skills for free when they install your library. [How →](https://mysticmind.github.io/agentskills-cli/tutorials/ship-skills-with-library)
- **Versioned install units** - skills from a NuGet or npm package are tracked together as a managed set. `agentskills-cli remove MyOrg.SkillPack -y` wipes all of them at once; `agentskills-cli update` checks the whole package for drift; `agentskills-cli list --by package` groups them. Same dependency-like semantics .NET devs already use for NuGet packages - no orphan skills when you uninstall.
- **Extension points by design** - register `ISkillSourceFactory` for new source types and `ISkillSearchProvider` for new search backends with a single DI registration.
- **Multi-targeted** - one `.nupkg` ships both .NET 8 LTS and .NET 10 builds.
- **Lock-file interop** - same `~/.agents/.skill-lock.json` and `./skills-lock.json` format as upstream `vercel-labs/skills`, so the two tools share state.

[Full feature matrix vs vercel-labs/skills →](https://mysticmind.github.io/agentskills-cli/reference/comparison)

## Documentation

Comprehensive guides at **[mysticmind.github.io/agentskills-cli](https://mysticmind.github.io/agentskills-cli/)**:

| Section | What's there |
|---|---|
| [Why AgentSkills CLI](https://mysticmind.github.io/agentskills-cli/why) | The value proposition, condensed |
| [Install](https://mysticmind.github.io/agentskills-cli/getting-started/install) | `dnx`, global tool, shell aliases, runtime requirements |
| [Quick start](https://mysticmind.github.io/agentskills-cli/getting-started/quick-start) | Five-minute hands-on tour |
| [Concepts](https://mysticmind.github.io/agentskills-cli/getting-started/concepts) | Skill, source, agent, scope - the five terms |
| [Commands](https://mysticmind.github.io/agentskills-cli/commands/add) | Full reference: `add`, `list`, `remove`, `init`, `find`, `update` |
| [Source formats](https://mysticmind.github.io/agentskills-cli/sources/) | Local, GitHub, GitLab, git, NuGet, npm, well-known endpoints |
| [Authoring + publishing](https://mysticmind.github.io/agentskills-cli/authoring/skill-format) | SKILL.md format + NuGet + npm publishing patterns |
| [Common workflows](https://mysticmind.github.io/agentskills-cli/tutorials/common-workflows) | Bootstrap a project, drive `dnx` in CI, set up private feeds, etc. |
| [Reference](https://mysticmind.github.io/agentskills-cli/reference/agents) | Agents, lock files, env vars, search-provider extension contract |
| [Troubleshooting](https://mysticmind.github.io/agentskills-cli/troubleshooting) | Common issues with fixes |
| [FAQ](https://mysticmind.github.io/agentskills-cli/faq) | The "why is it like that" questions |

## A .NET-native port of vercel-labs/skills

AgentSkills CLI is a faithful .NET port of [vercel-labs/skills](https://github.com/vercel-labs/skills) (the `npx skills` CLI). The two tools share the open [Agent Skills specification](https://agentskills.io), the same `SKILL.md` format, the same lock-file format, and the universal `.agents/skills/` install directory - so a skill installed by either tool is visible to the other and you can mix toolchains in a polyglot team.

AgentSkills CLI extends the upstream with first-class NuGet support, full npm registry fetch (not just `node_modules` sync), library-bundled skill packages, version-aware target matching on `list` / `remove`, and an extension-point architecture for custom sources and search backends. See the [full feature comparison](https://mysticmind.github.io/agentskills-cli/reference/comparison).

## Samples

Three working samples under [`samples/`](samples) - a minimal standalone skill, a NuGet package that bundles a skill, and an npm package that does the same. Each has its own README with copy-pasteable verify commands. See the [Samples reference](https://mysticmind.github.io/agentskills-cli/reference/samples) for what to try.

## Contributing

Issues and PRs welcome at [github.com/mysticmind/agentskills-cli](https://github.com/mysticmind/agentskills-cli). See [`CHANGELOG.md`](CHANGELOG.md) for release history.

To build from source:

```bash
git clone https://github.com/mysticmind/agentskills-cli.git
cd agentskills-cli
dotnet build agentskills-cli.sln
dotnet test agentskills-cli.sln
```

Requires the **.NET 10 SDK** (it can build both `net8.0` and `net10.0` outputs; the .NET 8 SDK cannot build the net10 output).

## License

MIT. See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).
