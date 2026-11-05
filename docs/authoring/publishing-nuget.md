# Publishing skills as a NuGet package

::: tip Two patterns work, pick what fits.

- **Skills-only package** - the package's only purpose is shipping skills. Examples: a team's curated prompts library, a consultancy's set of code-review skills. Use this when there's no underlying library to attach to.
- **Skills bundled into your existing library package** - add a `skills/` folder to the `.nupkg` you already publish. The skills version-lock with the library (no drift between an SDK release and its agent guidance), live at the same package id users already depend on (zero discovery friction), and reuse your existing publishing pipeline. A `Contoso.SampleLib.Skills.nupkg` is fine; a `Contoso.SampleLib.nupkg` that *contains* skills is often better.

:::

## The layout

Put each skill in its own folder under `skills/` at the **package root**:

```
Contoso.Widgets.nupkg
└── skills/
    ├── contoso-widgets-usage/
    │   ├── SKILL.md
    │   └── references/
    │       └── batching.md
    └── contoso-widgets-testing/
        └── SKILL.md
```

This is the layout NuGet's own [`dotnet-package-skills`](https://github.com/NuGet/Client.Tools/tree/main/dotnet-package-skills) tool reads, and AgentSkills CLI reads it too, so one package works with both tools.

Rules that keep a package working everywhere:

- **One folder per skill**, directly under `skills/`, each with its own `SKILL.md`. A lone `skills/SKILL.md` isn't picked up by `dotnet-package-skills`.
- **Prefix folder names with your package id**, so skills from different packages can't collide in a user's skills folder. Use the id lowercased with dots turned into hyphens (`Contoso.Widgets` becomes `contoso-widgets-...`). The Agent Skills spec allows only lowercase letters, digits, and hyphens in a skill name.
- **Make the folder name and the `name` in `SKILL.md` identical.** `dotnet-package-skills` installs a skill under its folder name and AgentSkills CLI under its `name`; when they match, both tools produce the same result. The spec requires them to match anyway.
- **Write a useful `description`.** It's what users see when they choose which skills to install.

```yaml
---
name: contoso-widgets-usage
description: >
  Correct usage patterns for Contoso.Widgets, including lifetime rules and batching.
  Use when creating, configuring, or disposing a Widget.
---
```

## Minimum `.csproj` for a skills-only package

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <NoWarn>$(NoWarn);NU5128;NU5127</NoWarn>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>

    <PackageId>MyOrg.AgentSkills</PackageId>
    <Version>1.0.0</Version>
    <Authors>My Org</Authors>
    <Description>Our team's curated agent skills.</Description>
    <PackageTags>agent-skills;skills</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>

  <ItemGroup>
    <!-- %(RecursiveDir) keeps each skill's folder structure; without it every file
         lands in one directory and the skill loses its reference documents. -->
    <None Include="skills/**/*" Pack="true" PackagePath="skills/%(RecursiveDir)%(Filename)%(Extension)" />
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

`IncludeBuildOutput=false` and `NoWarn=NU5128;NU5127` matter: this package has no managed assembly to ship, only skills, and the warnings about an empty `lib/` folder and missing dependencies aren't useful.

## Build and push

```bash
dotnet pack -o ./out
dotnet nuget push ./out/MyOrg.AgentSkills.1.0.0.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key $NUGET_API_KEY
```

Publishing from GitHub Actions? Prefer nuget.org [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): add a trusted publishing policy for your repo and workflow on nuget.org, give the job `id-token: write`, and get a short-lived key with the `NuGet/login` action instead of storing a long-lived API key in secrets:

```yaml
- uses: NuGet/login@v1
  id: login
  with:
    user: your-nuget-username
- run: dotnet nuget push ./out/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ steps.login.outputs.NUGET_API_KEY }}
```

Check the package before pushing: `unzip -l ./out/MyOrg.AgentSkills.1.0.0.nupkg` should list `skills/<name>/SKILL.md` for every skill.

Users install with:

```bash
agentskills-cli add MyOrg.AgentSkills -y
```

## Bundle into an existing library package

If you maintain a library NuGet package, **adding skills is one `ItemGroup` in the same `.csproj`**. No new package, no new publishing pipeline.

```
src/Contoso.Widgets/
├── Contoso.Widgets.csproj
├── (library code)
└── skills/
    ├── contoso-widgets-usage/
    │   └── SKILL.md
    └── contoso-widgets-testing/
        └── SKILL.md
```

Add to `Contoso.Widgets.csproj`:

```xml
<ItemGroup>
  <None Include="skills/**/*" Pack="true" PackagePath="skills/%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

On the next `dotnet pack`, the `.nupkg` contains the skills. Users who install `Contoso.Widgets` for the library get its skills at the same version, and `agentskills-cli update Contoso.Widgets` keeps them in step when they upgrade.

## Older layout: `contentFiles/any/any/skills/`

Earlier versions of this guide recommended `contentFiles/any/any/skills/<name>/SKILL.md`. AgentSkills CLI still reads it, but `dotnet-package-skills` doesn't, so move existing packages to `skills/` at the root on their next release. When a package has both folders, AgentSkills CLI uses `skills/`.

## Other locations

AgentSkills CLI falls back to a recursive scan, and users can point it anywhere with `agentskills-cli add YourPackage --path some/custom/path`. **Every non-standard layout is friction your users have to learn**, and other tools may not find it at all, so use `skills/` unless you have a hard reason not to.

## See the working sample

A complete example lives at [`samples/sample-nuget-package`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/sample-nuget-package), including pack instructions and a local-folder feed for testing.
