# `update`

Detect remote changes for tracked GitHub skills and reinstall them.

## Synopsis

```
agentskills-cli update [<name>...] [-g] [-p] [--check] [-y]
```

## Arguments

| Argument | Meaning |
|---|---|
| `<name>` | Limit the check to specific skill names. Omit to check everything tracked. |

## Flags

| Flag | Meaning |
|---|---|
| `-g`, `--global` | Only the global scope. |
| `-p`, `--project` | Only the project scope. |
| Both or neither | Both scopes (auto-detected based on the presence of `skills-lock.json` or `.agents/skills/`). |
| `--check` | Report drift only - do **not** install. Exit code is always 0; use the table output to decide. |
| `-y`, `--yes` | Skip the install confirmation. |

## How it works

[`add`](/commands/add) records the GitHub tree SHA (`skillFolderHash`) and skill folder path (`skillPath`) for every skill installed from a git source. `update` then:

1. Groups tracked skills by `owner/repo`.
2. Calls the GitHub Trees API once per repo.
3. Looks up each skill's current tree SHA in the response.
4. Flags any skill whose tree SHA changed since install.
5. (Unless `--check`) reinstalls the drifted skills.

## GitHub auth (lazy)

The token resolution chain:

1. Try unauthenticated (sufficient for most personal use - 60 req/hr per IP).
2. On a rate-limit 403, try `GITHUB_TOKEN`.
3. Then `GH_TOKEN`.
4. Then `gh auth token` (prints a one-time stderr note so you know).

Set `GITHUB_TOKEN` proactively if you hit rate limits regularly.

## Skipped sources

Sources that can't be checked automatically appear in a separate **Skipped** table with an explanation:

| Source type | Why it's skipped today |
|---|---|
| Local paths | Re-install manually to refresh |
| Generic git URLs | Re-install manually |
| GitLab | Trees API not implemented in v1 |
| NuGet | Re-install with a newer version to refresh |
| npm | Re-install with a newer version to refresh |
| Well-known endpoints | Re-install manually |
| GitHub but no folder hash | Installed before update tracking (pre-v0.2.0 leftover) |

This isn't a permanent limitation - GitLab Trees API support and similar are reasonable follow-ups. For now, the GitHub path is what's automated.

## Examples

```bash
# Dry-run: report drift, change nothing
agentskills-cli update --check

# Reinstall every drifted global skill
agentskills-cli update -g -y

# Only this one skill
agentskills-cli update web-design-guidelines -g
```

## Output

A drift table:

```
                  Updates available
╭───────────────────────┬────────┬──────────────────┬─────────┬─────────╮
│ Skill                 │ Scope  │ Source           │ Old     │ New     │
├───────────────────────┼────────┼──────────────────┼─────────┼─────────┤
│ web-design-guidelines │ global │ vercel-labs/…    │ 0000000 │ 3116f3e │
╰───────────────────────┴────────┴──────────────────┴─────────┴─────────╯
```

And, if `--check` was not passed:

```
1 update(s) available - run without --check to install.
Updating web-design-guidelines (global)...
[install table from `add` flow]
```
