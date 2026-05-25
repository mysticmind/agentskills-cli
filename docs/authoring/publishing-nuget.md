# Publishing skills as a NuGet package

::: tip Two patterns work, pick what fits.

- **Skills-only package** - the package's only purpose is shipping skills. Examples: a team's curated prompts library, a consultancy's set of code-review skills. Use this when there's no underlying library to attach to.
- **Skills bundled into your existing library package** - drop `contentFiles/any/any/skills/<name>/SKILL.md` into the same `.nupkg` you already publish. The skills version-lock with the library (no drift between an SDK release and its agent guidance), live at the same package id users already depend on (zero discovery friction), and reuse your existing publishing pipeline. A `Contoso.SampleLib.Skills.nupkg` is fine; a `Contoso.SampleLib.nupkg` that *contains* skills is often better.

:::

## The convention

AgentSkills uses the standard NuGet `contentFiles` layout:

```
my-skills.csproj
contentFiles/
└── any/
    └── any/
        └── skills/
            ├── skill-one/
            │   ├── SKILL.md
            │   └── reference.md
            └── skill-two/
                └── SKILL.md
```

The `contentFiles/any/any/skills/` path means "for any target framework, any language - just files for content." Users get them via AgentSkills, not via the C# compilation pipeline.

## Minimum `.csproj`

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
    <Content Include="contentFiles\any\any\skills\**\*">
      <Pack>true</Pack>
      <PackagePath>contentFiles\any\any\skills\</PackagePath>
      <BuildAction>None</BuildAction>
      <CopyToOutput>false</CopyToOutput>
    </Content>
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
</Project>
```

`IncludeBuildOutput=false` and `NoWarn=NU5128;NU5127` matter: this package has no managed assembly to ship, only content, and the warnings about empty `lib/` and missing dependencies aren't useful.

## Build and push

```bash
dotnet pack -o ./out
dotnet nuget push ./out/MyOrg.AgentSkills.1.0.0.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key $NUGET_API_KEY
```

Users install with:

```bash
agentskills-cli add MyOrg.AgentSkills -y
```

## Bundle into an existing library package

If you maintain a library NuGet package, **adding skills is a few extra lines in the same `.csproj`**. No new package, no new publishing pipeline.

Existing structure:

```
src/Contoso.SampleLib/
├── Contoso.SampleLib.csproj         # your normal library project
└── (library code)
```

Add a skills folder and ItemGroup:

```
src/Contoso.SampleLib/
├── Contoso.SampleLib.csproj
├── (library code)
└── contentFiles/
    └── any/
        └── any/
            └── skills/
                ├── samplelib-fundamentals/
                │   └── SKILL.md
                ├── samplelib-middleware/
                │   └── SKILL.md
                └── ...
```

Add to `Contoso.SampleLib.csproj`:

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

Next `dotnet pack`, the `.nupkg` contains the skills. Users who install `Contoso.SampleLib` for the library code get the skills as a bonus.

## Prefer the convention

`contentFiles/any/any/skills/<name>/SKILL.md` is auto-discovered with no extra flags - users just run `agentskills-cli add YourPackage` and it works. AgentSkills also falls back to a recursive scan, and users can install with `agentskills-cli add YourPackage --path some/custom/path` when skills live somewhere else, but **every non-conventional layout is friction the user has to learn**. Use the convention unless you have a hard reason not to.

## See the working sample

A complete example lives at [`samples/sample-nuget-package`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/sample-nuget-package). Includes pack + push instructions for a local file feed for testing.
