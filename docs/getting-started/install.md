# Install

AgentSkills CLI is a [.NET tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) targeting **.NET 8 LTS** and **.NET 10**. Pick whichever invocation style fits the moment.

## Option A: one-shot via `dnx` (.NET 10+)

No install step required. `dnx` is the .NET 10 SDK's equivalent of `npx` - it downloads the tool on first use, caches it, and runs it.

```bash
# --prerelease required while AgentSkills CLI is in the 0.2.0-preview phase
dnx agentskills-cli --prerelease --help
dnx agentskills-cli --prerelease -- add ./my-skill -a claude-code
```

When the stable `0.2.0` ships, `--prerelease` becomes optional - omit it to track stable, keep it to track previews.

Best when you want to try the tool once or run it in CI without polluting the global tool space.

## Option B: global tool

Install once, type `agentskills-cli` from anywhere.

```bash
dotnet tool install --global agentskills-cli
agentskills-cli --help
```

Update or uninstall:

```bash
dotnet tool update --global agentskills-cli
dotnet tool uninstall --global agentskills-cli
```

## Requirements

- **.NET 8 LTS or .NET 10 runtime** for the global-tool path. The package is multi-targeted; `dotnet tool install` picks the right build automatically.
- **`dnx agentskills-cli` requires .NET 10** specifically - `dnx` itself ships only with the .NET 10 SDK. On .NET 8 use the global-tool path.
- **`git` on PATH** when installing from git URLs (GitHub, GitLab, arbitrary git). Local and NuGet/npm sources don't need git.
- **Building from source** requires the .NET 10 SDK (it can build both target frameworks; the .NET 8 SDK cannot build the net10 output).

## Want a shorter command?

The shell command is intentionally `agentskills-cli` (not `skills`) so it doesn't shadow the `npx skills` binary on PATH. Alias it locally if you want:

::: code-group

```bash [bash / zsh]
# ~/.bashrc or ~/.zshrc
alias as=agentskills-cli
```

```fish [fish]
# ~/.config/fish/config.fish
alias as agentskills-cli
```

```powershell [PowerShell]
# $PROFILE
Set-Alias -Name as -Value agentskills-cli
```

:::

Then use `as add ./my-skill -y`, `as list`, etc.

### Same alias for installed-tool and dnx

The simple alias above only covers the installed-tool path. If you sometimes run via `dnx agentskills-cli` (CI runners, machines without the global install), use a shell function instead - it auto-detects which mode is available and falls back transparently:

::: code-group

```bash [bash / zsh]
# ~/.bashrc or ~/.zshrc
as() {
  if command -v agentskills-cli >/dev/null 2>&1; then
    agentskills-cli "$@"
  else
    dnx agentskills-cli --prerelease -- "$@"
  fi
}
```

```fish [fish]
# ~/.config/fish/config.fish
function as
  if type -q agentskills-cli
    agentskills-cli $argv
  else
    dnx agentskills-cli --prerelease -- $argv
  end
end
```

```powershell [PowerShell]
# $PROFILE
function as {
  if (Get-Command agentskills-cli -ErrorAction SilentlyContinue) {
    & agentskills-cli @args
  } else {
    & dnx agentskills-cli --prerelease -- @args
  }
}
```

:::

Now `as add ./my-skill -y` works whether you've installed the tool globally or are on a fresh `.NET 10` runner with only `dnx` available. Same six characters either way.

## Verify

After installing, confirm the version and that all commands are wired up:

```bash
agentskills-cli --help
```

You should see six commands: `add`, `list`, `remove`, `init`, `find`, `update`.

Next: [Concepts →](/getting-started/concepts)
