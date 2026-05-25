# Why AgentSkills

If you write .NET code and use Claude Code, Cursor, Codex, or any agent that reads the universal `.agents/skills/` directory, you want a CLI that feels native: installs from NuGet, respects your `NuGet.config`, ships through `dnx`. AgentSkills exists because that CLI didn't exist.

## The short version

- **Install agent skills the way .NET devs install everything else** - from NuGet (public or private), with your existing credentials and feeds. No new auth surface, no JavaScript runtime, no second package manager.
- **Skills can ship inside the libraries they describe.** A `Contoso.SampleLib` NuGet package can carry the agent guidance for using `Contoso.SampleLib`. Users who install your library get the skills automatically; the SDK and its agent docs version together, distribute together, stay in sync.
- **First-class npm too** - because TypeScript skills shipped on npm are still the bulk of the ecosystem, and AgentSkills is the only tool that does proper registry fetch with `.npmrc` scoped-registry support (not just `node_modules` sync).
- **Runs as `dnx`** - one-shot invocation on .NET 10, exactly like `npx`. CI runners need nothing pre-installed.

## The bigger insight: skills inside libraries

The standard pattern for AI-agent guidance today is *separate*. Library X exists; some helpful third party writes "how to use library X with Claude Code" skills and publishes them somewhere else. Drift is inevitable - X ships v2 with breaking changes, the skills still describe v1 behavior, agents make broken suggestions.

When skills live **inside** the library package:

- **One source of truth** - the skills version-lock with the SDK. v2 of the package ships v2 of the skills.
- **Zero discovery friction** - users searching for the library on nuget.org or npmjs.org get the skills for free. No "you should also install MyLib.Skills" footnote in the README.
- **Standard publishing pipeline** - `dotnet pack` or `npm publish`. Nothing new to learn.
- **Agent reach** - any agent that reads the universal `.agents/skills/` directory picks up the guidance once the user installs.

The dedicated skills-only package is still a fine option when there's no library to co-ship with. But "drop a `skills/` folder into the package you already publish" is a meaningfully better default.

[How to ship skills inside your library →](/tutorials/ship-skills-with-library)

## How it compares to `npx skills`

AgentSkills is a faithful .NET-native port of [`vercel-labs/skills`](https://github.com/vercel-labs/skills) that adds first-class NuGet and npm support plus ergonomics. The highlights:

| | AgentSkills | `npx skills` |
|---|---|---|
| **NuGet packages** as a first-class source | yes | no NuGet path |
| **npm registry fetch** (not just `node_modules` sync) | yes | only `experimental_sync` |
| **Skills can ship inside library packages** | yes | n/a (no NuGet); npm only via experimental sync |
| **`--path` across every source** (NuGet, npm, git, local) | yes | only `/tree/<ref>/<path>` for GitHub |
| **Version-aware target matching** on `list` / `remove` | yes | no concept of pinned matching |
| **DI extension points** for custom sources + search backends | yes | procedural, no public extension contract |
| **Runtime** | .NET 8 LTS + .NET 10 from one .nupkg | Node 18+ |

[Full feature matrix →](/reference/comparison)

## Lock-file interop, not lock-in

By design, AgentSkills writes to the **same** `~/.agents/.skill-lock.json` and `./skills-lock.json` files that `npx skills` uses. A skill installed by either tool is visible to the other. You can switch tools without losing tracked state, you can use both on the same machine, and a polyglot team can have `npx skills` users and `agentskills-cli` users in the same repo with no friction.

## Built on the open spec

The `SKILL.md` format and the well-known discovery endpoint follow the open [Agent Skills specification](https://agentskills.io). Skills published for `npx skills`, the broader ecosystem, or any other spec-compliant client work here too. AgentSkills implements the spec; it doesn't fork it.

## Next

- [Install](/getting-started/install) - get the CLI on your machine
- [Quick start](/getting-started/quick-start) - five-minute hands-on tour
- [Concepts](/getting-started/concepts) - the five terms you need to know
- [Full feature comparison](/reference/comparison) - every difference vs `npx skills`
- [Ship skills with your library](/tutorials/ship-skills-with-library) - the library-shipping pattern in depth
