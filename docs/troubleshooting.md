# Troubleshooting

## `dnx: command not found`

`dnx` ships with .NET 10 only. Either:

- Install the .NET 10 SDK, or
- Use the global-tool path: `dotnet tool install --global agentskills && agentskills …` (works on .NET 8+)

## `The framework 'Microsoft.NETCore.App', version '10.0.0' was not found`

Your installed tool is the net10 build but only .NET 8 is present. Reinstall:

```bash
dotnet tool uninstall -g agentskills
dotnet tool install -g agentskills
```

NuGet will pick the net8 build automatically. Or install the .NET 10 runtime side-by-side with .NET 8.

## `git: command not found` during `agentskills add owner/repo`

Install git and put it on `PATH`. Local and NuGet/npm sources don't need git; only sources from GitHub / GitLab / arbitrary git URLs.

## `NuGet sources: No enabled NuGet sources found`

Your `NuGet.Config` lists no enabled feeds. Run:

```bash
dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
```

…or pass `--nuget-source <URL>` to point at a specific feed for this command.

## Private NuGet feed asks for credentials

Make sure the appropriate credential provider is installed:

- **Azure Artifacts**: install the [Azure Artifacts Credential Provider](https://github.com/microsoft/artifacts-credprovider)
- **GitHub Packages**: add your PAT to `NuGet.Config` as documented by GitHub
- **Other**: follow the feed vendor's NuGet auth docs

AgentSkills uses `NuGet.Protocol` and doesn't introduce any new auth surface. If `dotnet restore` works against your feed, `agentskills add` will too.

## `update` shows "Could not fetch tree (rate-limited, private, or moved)"

GitHub allows 60 unauthenticated requests per hour per IP. Set `GITHUB_TOKEN` (or `GH_TOKEN`) and re-run:

```bash
export GITHUB_TOKEN=ghp_...
agentskills update -g
```

If you have `gh` CLI authenticated, AgentSkills will fall back to `gh auth token` automatically after a 403 - no env var needed.

## Symlinks failing on Windows

Pass `--copy` (the default) or enable Developer Mode in Windows Settings. The installer falls back to copy automatically when symlink creation fails.

## Tests can't bind to a TCP port

The well-known source tests start a short-lived `HttpListener`. Re-run if a port races - the tests pick a free port each time.

## Want a shorter command than `agentskills`?

Alias it in your shell rc. The tool deliberately ships under `agentskills` (not `skills`) so it doesn't shadow `npx skills` when both are installed, but you can pick any short name you like locally:

::: code-group

```bash [bash / zsh]
# ~/.bashrc or ~/.zshrc
alias as=agentskills
```

```fish [fish]
# ~/.config/fish/config.fish
alias as agentskills
```

```powershell [PowerShell]
# $PROFILE
Set-Alias -Name as -Value agentskills
```

:::

After that: `as add ./my-skill -y`, `as list --by package`, etc.

## `agentskills` and `npx skills` coexistence

If you previously installed `npx skills` and now also have `agentskills` installed, the two coexist by design: separate binaries (`skills` vs `agentskills`), but they share the same lock file (`~/.agents/.skill-lock.json`) and install directory (`.agents/skills/`). Installs done by either tool are visible to both.
