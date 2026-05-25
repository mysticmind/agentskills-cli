# Environment variables

All optional. Reasonable defaults apply if unset.

| Variable | Effect |
|---|---|
| `CLAUDE_CONFIG_DIR` | Override Claude home (default `~/.claude`). |
| `CODEX_HOME` | Override Codex home (default `~/.codex`). |
| `XDG_CONFIG_HOME` | XDG base used for OpenCode and Universal global dirs (default `~/.config`). |
| `XDG_STATE_HOME` | If set, the global lock lives at `$XDG_STATE_HOME/skills/.skill-lock.json` instead of `~/.agents/.skill-lock.json`. |
| `SKILLS_CLONE_TIMEOUT_MS` | Hard timeout for `git clone` in milliseconds (default `300000` = 5 min). |
| `INSTALL_INTERNAL_SKILLS` | Set to `1` or `true` to include skills with `metadata.internal: true` in scans. |
| `SKILLS_API_URL` | Override the search backend used by `find` (default `https://skills.sh`). |
| `GITHUB_TOKEN` / `GH_TOKEN` | Used by `update` only after a rate-limit 403. Silent fallback. |

## Git environment

`git clone` is invoked with these environment variables set, regardless of your shell config:

| Variable | Value | Why |
|---|---|---|
| `GIT_TERMINAL_PROMPT` | `0` | Never block on credential prompts. Auth must be configured up front. |
| `GIT_LFS_SKIP_SMUDGE` | `1` | Skip LFS object download - skills are small and LFS-fetched content slows things down for no benefit |

This means an `agentskills-cli add` against a private repo without configured credentials will fail fast (no prompt), not hang.

## Verbosity flags

These are CLI flags rather than env vars, but worth noting alongside:

| Flag | Effect |
|---|---|
| `-v`, `--verbose` | Raise minimum log level to Debug (shows DI resolution, source resolution, etc.) |
| `-q`, `--quiet` | Lower minimum log level to Warning (suppresses info notes) |
| `--trace` | Lowest minimum log level. Lots of output. |
| (none) | Default Information level |
