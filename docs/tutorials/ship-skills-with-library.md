# Ship skills with your library

If you maintain a NuGet or npm library, **you can ship Claude Code / Cursor / Codex guidance inside the same package you already publish.** Users who install your library get the skills as a side effect. The SDK and the agent docs version together, distribute together, and stay in sync without a separate publishing pipeline.

This tutorial walks through the pattern for both NuGet and npm.

## Why this matters

The standard pattern for AI-agent guidance today is *separate*. Library X exists; some helpful third party writes "how to use library X with Claude Code" skills and publishes them somewhere else. Drift is inevitable: X ships v2 with breaking changes, the skills still describe v1 behavior, agents make broken suggestions.

When skills live **inside** the library package:

- **One source of truth** - the skills version-lock with the SDK. v2 of the package ships v2 of the skills.
- **Zero discovery friction** - users searching for the library on nuget.org / npmjs.org get the skills for free. No "you should also install MyLib.Skills" footnote in the README.
- **Standard publishing pipeline** - `dotnet pack` or `npm publish`. Nothing new to learn.
- **Agent reach** - any agent that reads `.agents/skills/` (which is most of them, via the universal convention) picks up the guidance once the user installs.

## Pattern 1: NuGet

Assume you maintain `Contoso.SampleLib`. Add a `contentFiles` path:

```
src/Contoso.SampleLib/
├── Contoso.SampleLib.csproj
├── (library code)
└── contentFiles/
    └── any/
        └── any/
            └── skills/
                ├── samplelib-handlers/SKILL.md
                └── samplelib-middleware/SKILL.md
```

Add to the `.csproj`:

```xml
<ItemGroup>
  <Content Include="contentFiles\any\any\skills\**\*">
    <Pack>true</Pack>
    <PackagePath>contentFiles\any\any\skills\</PackagePath>
    <BuildAction>None</BuildAction>
    <CopyToOutput>false</CopyToOutput>
  </Content>
</ItemGroup>
```

Next `dotnet pack`, the `.nupkg` ships with the skills. Users:

```bash
agentskills-cli add Contoso.SampleLib -y
```

…and the skills land in their `.agents/skills/`, immediately readable by Claude Code, Cursor, Codex, OpenCode, etc.

## Pattern 2: npm

Assume you maintain `@my-org/sample-toolkit`. Add a top-level `skills/`:

```
@my-org/sample-toolkit/
├── package.json
├── dist/
├── src/
└── skills/
    ├── sample-toolkit-components/SKILL.md
    └── sample-toolkit-hooks/SKILL.md
```

Update `package.json` to include `skills/` in the published files:

```diff
{
  "name": "@my-org/sample-toolkit",
  "version": "2.5.0",
- "files": ["dist/", "README.md"]
+ "files": ["dist/", "README.md", "skills/"]
}
```

Next `npm publish`, the tarball ships with the skills. Users:

```bash
agentskills-cli add @my-org/sample-toolkit -y
```

## What to write in your skills

Each `SKILL.md` is markdown with YAML frontmatter (see [SKILL.md format](/authoring/skill-format)). For library skills, a good template:

```markdown
---
name: samplelib-handlers
description: Configure Contoso.SampleLib handlers, including the
  conventional registration pattern and middleware composition rules.
---
# samplelib-handlers

When working with Contoso.SampleLib, agents should follow these
conventions for handler registration and middleware composition.

## When to invoke

- Adding a new handler class
- Configuring middleware order
- Troubleshooting handler discovery failures

## Conventions

[Specific, current API guidance. Update this when your API changes.]

## Common mistakes

- Don't [thing X]. Instead, [thing Y].
- Always [thing Z].
```

Keep skills focused. One concept per skill is easier to maintain than a single sprawling "how to use everything" file.

## Versioning

Because the skills version-lock with the library, no extra versioning is needed - the user already pins the library version, the skills come along. If they upgrade `Contoso.SampleLib` from 2.5.0 to 3.0.0 with breaking API changes, the skills update at the same time.

When breaking changes happen:

1. Update the affected `SKILL.md` files to describe the new API
2. Bump the package version
3. Publish

Users who run `agentskills-cli update` after upgrading the library will see the skill folder hash change and pull the updated skills automatically.

## Try it locally first

Test the pattern before publishing to a real feed. For NuGet:

```bash
dotnet pack src/Contoso.SampleLib -o ./local-feed
agentskills-cli add Contoso.SampleLib \
  --nuget-source ./local-feed -a universal -y --copy
ls .agents/skills/
```

For npm with [Verdaccio](https://verdaccio.org/):

```bash
# Terminal 1
npx verdaccio

# Terminal 2
cd @my-org/sample-toolkit
npm publish --registry http://localhost:4873

# Terminal 3 (your test project)
agentskills-cli add @my-org/sample-toolkit \
  --npm-registry http://localhost:4873 -a universal -y --copy
ls .agents/skills/
```

Both flows let you validate the layout before committing to a public publish.

## Next

- [Publishing to NuGet](/authoring/publishing-nuget) - full publishing guide
- [Publishing to npm](/authoring/publishing-npm) - full publishing guide
- [SKILL.md format](/authoring/skill-format) - the schema for individual skills
