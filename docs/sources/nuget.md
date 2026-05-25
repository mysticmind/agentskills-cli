# NuGet

Install skills from any NuGet feed - public nuget.org or private (Azure Artifacts, GitHub Packages, JFrog, your own).

## Input shapes

```bash
# Bare shorthand (auto-detected when the id contains a '.')
agentskills-cli add MyOrg.AgentSkills
agentskills-cli add MyOrg.AgentSkills@1.2.3

# Explicit prefix (required for IDs without a '.')
agentskills-cli add nuget:MyOrg.AgentSkills
agentskills-cli add nuget:MyOrg.AgentSkills@1.2.3

# Local .nupkg file (path-based, skips feed lookup)
agentskills-cli add ./Contoso.SampleSkills.1.4.0.nupkg
agentskills-cli add /abs/path/to/Contoso.SampleSkills.1.4.0.nupkg
```

## Detection rule

A source is treated as NuGet if it:

- starts with `nuget:`, OR
- matches the NuGet shorthand regex (contains a `.`, no `/`, no `:`), OR
- is a local path ending in `.nupkg`.

That second rule means single-segment NuGet IDs without a dot (rare in practice) need the explicit `nuget:` prefix - otherwise they fall through to the git fallback.

## Local .nupkg files

Pass a local `.nupkg` file path (relative or absolute) and AgentSkills CLI extracts it directly - no feed lookup, no network call. Package id + version are read from the embedded `.nuspec`, so the lock entry looks identical to a feed-resolved install:

```bash
# After `dotnet pack` produces ./bin/Release/Contoso.SampleSkills.1.4.0.nupkg
agentskills-cli add ./bin/Release/Contoso.SampleSkills.1.4.0.nupkg -y
agentskills-cli list --by package
# -> Contoso.SampleSkills @ 1.4.0
```

Useful for testing skill packages before publishing to a feed, installing packages from sneakernet / air-gapped environments, or wiring up CI verification steps without standing up a local feed first.

## Feeds and auth

AgentSkills CLI uses `NuGet.Protocol` with `Settings.LoadDefaultSettings()`, so **every feed listed in your machine, user, or project `NuGet.Config` is searched in order**. Credential providers (Azure Artifacts Credential Provider, GitHub Packages PAT in NuGet.Config, etc.) are honored automatically - no flag needed.

If `dotnet restore` works against your feed, `agentskills-cli add <pkg>` works against the same feed.

Override the feed list for a single command with `--nuget-source <URL>`:

```bash
agentskills-cli add MyOrg.AgentSkills \
  --nuget-source https://pkgs.contoso.com/v3/index.json
```

## Package layout

The convention is the standard NuGet `contentFiles` path:

```
mypackage.nupkg
└─ contentFiles/any/any/skills/<skill-name>/SKILL.md
                                          /...other files
```

Multiple skills per package allowed; each subfolder with a `SKILL.md` is its own skill.

If the package doesn't follow the convention, AgentSkills CLI falls back to a recursive scan inside the extracted `.nupkg`. Pass [`--path <subdir>`](/commands/add) to point discovery somewhere specific.

## Versions

A bare package id matches the latest stable version; pin with `@`:

```bash
agentskills-cli add MyOrg.AgentSkills              # latest stable
agentskills-cli add MyOrg.AgentSkills@1.2.3        # specific version
agentskills-cli add MyOrg.AgentSkills@2.0.0-beta.1 # prerelease
```

## Authoring NuGet packages with skills

See [Publishing to NuGet](/authoring/publishing-nuget) for the full guide, including how to add a `skills/` folder to an existing library package so the SDK and its agent guidance ship together.
