# Samples

Working samples live under [`samples/`](https://github.com/mysticmind/agentskills-cli/tree/main/samples) in the repo. Each is self-contained and exercised by smoke-test instructions in its own README. Use them to verify a local build, learn the file layouts, or copy the structure into your own packages.

## At a glance

| Sample | What it shows | Try it |
|---|---|---|
| [`samples/hello-skill`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/hello-skill) | The minimal valid skill: one folder, one `SKILL.md` with required frontmatter. | `agentskills add ./samples/hello-skill -a universal -y --copy` |
| [`samples/sample-nuget-package`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/sample-nuget-package) | A `.csproj` that packs one skill into the standard `contentFiles/any/any/skills/` NuGet layout. Includes pack + push instructions for a local file feed. | `dotnet pack samples/sample-nuget-package -o ./local-feed && agentskills add Sample.SkillPackage --nuget-source ./local-feed -a universal -y --copy` |
| [`samples/sample-npm-package`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/sample-npm-package) | A `package.json` plus `skills/<name>/SKILL.md` showing the npm-tarball layout (`package/skills/...` on the wire). Sample README covers Verdaccio-based local testing without publishing to npmjs.org. | `npm pack samples/sample-npm-package` (then publish to Verdaccio or npmjs.org and `agentskills add @acme/agent-skills-sample`) |

Each sample's own README has the full step-by-step verification commands.

## When to look at which sample

- **Authoring your first standalone skill?** Start with [`hello-skill`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/hello-skill). It is the minimum viable shape - one `SKILL.md`, the required frontmatter, nothing else. The [SKILL.md format](/authoring/skill-format) page explains every field.
- **Adding skills to a .NET library you publish to NuGet?** Open [`sample-nuget-package`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/sample-nuget-package). The `.csproj` shows the `<Content Include="contentFiles\...\skills\**" Pack="true">` pattern that any existing library can adopt. See [Publishing to NuGet](/authoring/publishing-nuget) for the broader pattern.
- **Adding skills to an npm package?** Open [`sample-npm-package`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/sample-npm-package). The `package.json` `"files": ["skills/"]` entry is the whole publishing trick. See [Publishing to npm](/authoring/publishing-npm) for Verdaccio-based local testing.

## Smoke-testing AgentSkills itself

The `hello-skill` sample is the easiest end-to-end test that AgentSkills works on your machine. After installing the tool:

```bash
git clone https://github.com/mysticmind/agentskills-cli.git
cd agentskills-cli
agentskills add ./samples/hello-skill -a universal -y --copy
agentskills list
```

You should see `hello-skill` in the list. Remove with `agentskills remove hello-skill -y`.

## Next

- [SKILL.md format](/authoring/skill-format) - schema reference
- [Publishing to NuGet](/authoring/publishing-nuget) - full pattern for library co-shipping
- [Publishing to npm](/authoring/publishing-npm) - the npm equivalent
- [Ship skills with your library](/tutorials/ship-skills-with-library) - the marketing-pitch tutorial
