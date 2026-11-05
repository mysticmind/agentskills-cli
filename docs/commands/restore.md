# `restore`

Install every skill recorded in `./skills-lock.json`, at the locked version or ref. Use it after cloning a repo, or in CI, the way you'd use `dotnet restore` or `npm ci`.

## Synopsis

```
agentskills-cli restore [-a agent...] [-y] [--copy] [--force] [--frozen]
                        [--nuget-source <URL>] [--npm-registry <URL>]
```

## Flags

| Flag | Meaning |
|---|---|
| `-a`, `--agent <NAME>` | Install for these agents. Default: the agents detected on this machine (prompted when interactive, all of them with `-y`). Falls back to `universal` when none are detected. |
| `-y`, `--yes` | Don't prompt. |
| `--copy` | Copy files into each agent directory instead of symlinking. |
| `--force` | Reinstall skills that are already present. By default they're left alone. |
| `--frozen` | Fail instead of installing when a source's content no longer matches the `computedHash` in the lock. Use this in CI. |
| `--nuget-source <URL>` | Restore NuGet packages from this feed only. |
| `--npm-registry <URL>` | Restore npm packages from this registry. |

## What it installs

| Lock `sourceType` | Restored from |
|---|---|
| `nuget`, `npm` | The exact locked version (`Package@1.2.0`) from your configured feeds and registries. |
| `github`, `gitlab` | The recorded repo at the recorded ref. GitLab entries resolve against gitlab.com. |
| `git`, `well-known` | The recorded URL (and ref). |
| `local` | The recorded path, so it only works when that path exists on this machine. |

Packages that were added from a local `.nupkg` or `.tgz` file are restored from your feeds by id and version; publish them to a feed (even a local folder feed with `--nuget-source ./feed`) for restore to work on other machines.

Skills installed under another name (`add --as`) are restored under that name.

## Content drift

`restore` compares each fetched skill with the `computedHash` in the lock:

- **Default**: installs it anyway, prints a warning, and updates the hash in the lock.
- **`--frozen`**: refuses to install it and exits `1`.

Pinned packages never drift. Git sources drift when the recorded ref is a branch that moved; pin a tag or commit if you need byte-for-byte restores.

## Examples

```bash
# Fresh clone
git clone https://github.com/contoso/app && cd app
agentskills-cli restore -y

# CI: exact restore for Claude Code, fail on drift
agentskills-cli restore -a claude-code -y --frozen

# Rebuild everything from scratch
agentskills-cli restore --force -y
```

Exit code is `1` when any skill failed to restore, otherwise `0`.
