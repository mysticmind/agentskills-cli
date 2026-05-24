# Install

AgentSkills is a [.NET tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) targeting **.NET 8 LTS** and **.NET 10**. Pick whichever invocation style fits the moment.

## Option A: one-shot via `dnx` (.NET 10+)

No install step required. `dnx` is the .NET 10 SDK's equivalent of `npx` - it downloads the tool on first use, caches it, and runs it.

```bash
dnx agentskills --help
dnx agentskills add ./my-skill -a claude-code
```

Best when you want to try the tool once or run it in CI without polluting the global tool space.

## Option B: global tool

Install once, type `agentskills` from anywhere.

```bash
dotnet tool install --global agentskills
agentskills --help
```

Update or uninstall:

```bash
dotnet tool update --global agentskills
dotnet tool uninstall --global agentskills
```

## Requirements

- **.NET 8 LTS or .NET 10 runtime** for the global-tool path. The package is multi-targeted; `dotnet tool install` picks the right build automatically.
- **`dnx agentskills` requires .NET 10** specifically - `dnx` itself ships only with the .NET 10 SDK. On .NET 8 use the global-tool path.
- **`git` on PATH** when installing from git URLs (GitHub, GitLab, arbitrary git). Local and NuGet/npm sources don't need git.
- **Building from source** requires the .NET 10 SDK (it can build both target frameworks; the .NET 8 SDK cannot build the net10 output).

## Want a shorter command?

The shell command is intentionally `agentskills` (not `skills`) so it doesn't shadow the `npx skills` binary on PATH. Alias it locally if you want:

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

Then use `as add ./my-skill -y`, `as list`, etc.

## Verify

After installing, confirm the version and that all commands are wired up:

```bash
agentskills --help
```

You should see six commands: `add`, `list`, `remove`, `init`, `find`, `update`.

Next: [Concepts →](/getting-started/concepts)
