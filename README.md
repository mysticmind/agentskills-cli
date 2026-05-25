<p align="center">
  <img src="https://raw.githubusercontent.com/mysticmind/agentskills-cli/main/docs/public/logo.png" alt="AgentSkills CLI" width="128" />
</p>

<h1 align="center">AgentSkills CLI</h1>

<p align="center">
  <strong>The .NET commandline tool for the open <a href="https://agentskills.io">Agent Skills</a> ecosystem.</strong>
</p>

<p align="center">
  Install skills from GitHub, NuGet, npm, well-known endpoints, or local folders into Claude Code, Cursor, Codex, OpenCode, and any spec-compliant agent.
</p>

<p align="center">
  First-class NuGet &amp; npm · library-bundled skill packages · extension points by design.
</p>

<p align="center">
  <a href="https://mysticmind.github.io/agentskills-cli/"><strong>📖 Documentation</strong></a> &nbsp;·&nbsp;
  <a href="https://mysticmind.github.io/agentskills-cli/why">Why AgentSkills CLI?</a> &nbsp;·&nbsp;
  <a href="https://mysticmind.github.io/agentskills-cli/getting-started/quick-start">Quick start</a> &nbsp;·&nbsp;
  <a href="https://mysticmind.github.io/agentskills-cli/reference/comparison">vs <code>npx skills</code></a>
</p>

---

## Install

```bash
# One-shot via dnx (.NET 10+)
dnx agentskills-cli --help

# Or as a global tool (.NET 8 LTS or .NET 10)
dotnet tool install --global agentskills-cli
agentskills-cli --help
```

See the [install guide](https://mysticmind.github.io/agentskills-cli/getting-started/install) for `.NET 8` notes, shell aliases (including a function that auto-falls-back to `dnx`), and verification.

## At a glance

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

[Five-minute quick start →](https://mysticmind.github.io/agentskills-cli/getting-started/quick-start)

## What makes it different

- **NuGet as a first-class source** - public *and* private feeds via your existing `NuGet.config` and credential providers. No new auth surface.
- **npm registry fetch** (not just `node_modules` sync) - public *and* private registries via your existing `.npmrc`, scoped registries, `_authToken`.
- **Skills can ship inside library packages** - drop a `skills/` folder into your existing `.nupkg` or `.tgz`. Users get the skills for free when they install your library. [How →](https://mysticmind.github.io/agentskills-cli/tutorials/ship-skills-with-library)
- **Extension points by design** - register `ISkillSourceFactory` for new source types and `ISkillSearchProvider` for new search backends with a single DI registration.
- **Multi-targeted** - one `.nupkg` ships both .NET 8 LTS and .NET 10 builds.
- **Lock-file interop** - same `~/.agents/.skill-lock.json` and `./skills-lock.json` format as upstream `vercel-labs/skills`, so the two tools share state.

[Full feature matrix vs `npx skills` →](https://mysticmind.github.io/agentskills-cli/reference/comparison)

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

## Built on the open spec

Implements the open [Agent Skills specification](https://agentskills.io) - the `SKILL.md` format and the well-known discovery endpoint - so skills published for the spec by anyone work here too. AgentSkills CLI implements the spec; it doesn't fork it.

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
