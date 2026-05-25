# Install

AgentSkills CLI is a [.NET tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) targeting **.NET 8 LTS** and **.NET 10**. Pick whichever invocation style fits the moment.

## Option A: global tool (recommended for daily use)

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

## Option B: one-shot via `dnx` (CI / no-install scenarios)

No install step required. `dnx` is the .NET 10 SDK's equivalent of `npx` - it downloads the tool on first use, caches it, and runs it.

```bash
dnx agentskills-cli -- --help
dnx agentskills-cli -- add ./my-skill -a claude-code
```

The `--` separator passes args after it through to the tool (not to `dnx`).

Best when you want to try the tool once, run it in CI without polluting the global tool space, or pin to a specific version (`dnx agentskills-cli@0.2.0 -- add ...`). For interactive daily use, prefer Option A - it's much less typing.

## Requirements

- **.NET 8 LTS or .NET 10 runtime** for the global-tool path. The package is multi-targeted; `dotnet tool install` picks the right build automatically.
- **`dnx agentskills-cli` requires .NET 10** specifically - `dnx` itself ships only with the .NET 10 SDK. On .NET 8 use the global-tool path.
- **`git` on PATH** when installing from git URLs (GitHub, GitLab, arbitrary git). Local and NuGet/npm sources don't need git.
- **Building from source** requires the .NET 10 SDK (it can build both target frameworks; the .NET 8 SDK cannot build the net10 output).

## Shortcuts

Typing `agentskills-cli add ./my-skill` (or worse, `dnx agentskills-cli -- add ./my-skill`) gets old fast. Three alias patterns to pick from depending on which install path you use.

### Pattern 1: alias the installed tool (most common)

If you're using the global tool (Option A), this is all you need:

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

Then `as add ./my-skill -y`, `as list`, etc.

### Pattern 2: alias the dnx invocation (no global install)

If you don't want a global install (CI sandboxes, ephemeral environments), alias the full dnx ceremony as a one-word shortcut:

::: code-group

```bash [bash / zsh]
# ~/.bashrc or ~/.zshrc
alias as='dnx agentskills-cli --'
```

```fish [fish]
# ~/.config/fish/config.fish
alias as 'dnx agentskills-cli --'
```

```powershell [PowerShell]
# $PROFILE
function as { & dnx agentskills-cli -- @args }
```

:::

Then `as add ./my-skill -y` runs the full `dnx agentskills-cli -- add ./my-skill -y` command behind the scenes.

### Pattern 3: auto-detect (works in both modes)

If you switch between machines (some have the global install, some don't), use a shell function that detects which path is available and falls back to dnx transparently:

::: code-group

```bash [bash / zsh]
# ~/.bashrc or ~/.zshrc
as() {
  if command -v agentskills-cli >/dev/null 2>&1; then
    agentskills-cli "$@"
  else
    dnx agentskills-cli -- "$@"
  fi
}
```

```fish [fish]
# ~/.config/fish/config.fish
function as
  if type -q agentskills-cli
    agentskills-cli $argv
  else
    dnx agentskills-cli -- $argv
  end
end
```

```powershell [PowerShell]
# $PROFILE
function as {
  if (Get-Command agentskills-cli -ErrorAction SilentlyContinue) {
    & agentskills-cli @args
  } else {
    & dnx agentskills-cli -- @args
  }
}
```

:::

`as add ./my-skill -y` works whether you've installed the tool globally or are on a fresh `.NET 10` runner with only `dnx` available - same six characters either way.

## Verify

After installing, confirm the version and that all commands are wired up:

```bash
agentskills-cli --help
```

You should see six commands: `add`, `list`, `remove`, `init`, `find`, `update`.

Next: [Concepts →](/getting-started/concepts)
