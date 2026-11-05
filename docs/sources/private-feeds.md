# Private feeds and registries

Skills often live on a private NuGet feed or npm registry: Azure Artifacts, GitHub Packages, ProGet, Artifactory, a folder on a share. AgentSkills CLI keeps the settings for those **separate from your normal .NET and npm setup**, so adding a skills feed never changes how your projects restore or install.

## Quick start

```bash
# NuGet: the team's skills feed, for everyone working on this repo (commit the file)
agentskills-cli feed add https://pkgs.contoso.com/v3/index.json --name contoso

# Your credentials for it, on your machine only
agentskills-cli feed add https://pkgs.contoso.com/v3/index.json --name contoso -g \
  --username you@contoso.com --password $TOKEN --store-password-in-clear-text

# npm: a scoped registry, with the token read from an environment variable
agentskills-cli registry add https://npm.contoso.com/ --scope @contoso --token-env CONTOSO_NPM_TOKEN

# Then, as usual, with no extra flags:
agentskills-cli add Contoso.AgentSkills -y
agentskills-cli add @contoso/agent-skills -y
```

## Where the settings live

| File | Scope | Commit it? | What goes in it |
|---|---|---|---|
| `.agents/nuget.config` | This project | Yes | NuGet feeds for skills. No credentials. |
| `~/.agents/nuget.config` | You, every project | No | Personal feeds and credentials. |
| `.agents/.npmrc` | This project | Yes | npm registries for skills. Tokens only as `${ENV_VAR}` references. |
| `~/.agents/.npmrc` | You, every project | No | Personal registries and tokens. |

They use the **standard file formats**: `nuget.config` is NuGet's own format and `.npmrc` is npm's. Anything those formats support works, for example package source mapping and `<clear />` in `nuget.config`, or scoped registries in `.npmrc`. You can edit them by hand, or use [`feed`](/commands/feed) and [`registry`](/commands/registry).

The tool refuses to write a password or token into a project file. User files that hold credentials are made readable only by you on Linux and macOS.

## Which feeds are used

**NuGet**, highest precedence first:

1. `--nuget-source` on the command (a URL, a folder, or the **name** of a configured feed)
2. `.agents/nuget.config` in the project
3. `~/.agents/nuget.config`
4. Your normal NuGet configuration: `nuget.config` files from the project folder upwards, then your user and machine-wide files (the same files `dotnet restore` reads)

A `<clear />` in a skills file hides every feed below it, so the project can say "skills come only from this feed":

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="contoso" value="https://pkgs.contoso.com/v3/index.json" />
  </packageSources>
</configuration>
```

**npm**, read in this order with later files winning: `~/.npmrc`, the project's `.npmrc`, `~/.agents/.npmrc`, then `.agents/.npmrc`. `--npm-registry` overrides the default registry for one command.

Check what's in effect, and where each setting comes from:

```bash
agentskills-cli feed list
agentskills-cli registry list
```

## Credentials

**NuGet**, in order of preference:

1. **A credential provider.** Install the [Azure Artifacts Credential Provider](https://github.com/microsoft/artifacts-credprovider) (or your feed vendor's) once; the tool uses it the same way `dotnet restore` does. Nothing to store.
2. **Credentials in your user file**: `feed add ... -g --username ... --password ...`. Outside Windows, NuGet can't encrypt them, so `--store-password-in-clear-text` is required.
3. **Credentials in your normal NuGet configuration** (for example from `dotnet nuget add source -u -p`) are used too, matched by feed name.

**npm**: tokens go in `~/.agents/.npmrc` with `registry add ... -g --token ...`, or in any file as an environment-variable reference with `--token-env NAME`, which is the right choice for CI and for committed files.

## The lock remembers where packages came from

When you install a NuGet or npm package, `skills-lock.json` records the feed or registry it came from (`sourceUrl`). [`update`](/commands/update), [`outdated`](/commands/outdated) and [`restore`](/commands/restore) try that feed first and fall back to the others, so a package is fetched from the same place it was installed from. A feed folder inside the project is recorded relative to the project root, so the lock works on every clone.

## In CI

```yaml
- run: agentskills-cli restore -y --frozen
  env:
    CONTOSO_NPM_TOKEN: ${{ secrets.CONTOSO_NPM_TOKEN }}   # used by ${CONTOSO_NPM_TOKEN} in .agents/.npmrc
```

For NuGet feeds, install your vendor's credential provider in the job, or add credentials to the runner's user file in a setup step:

```bash
agentskills-cli feed add https://pkgs.contoso.com/v3/index.json --name contoso -g \
  --username ci --password "$NUGET_TOKEN" --store-password-in-clear-text
```
