# GitHub

The most common remote source. Multiple input shapes all canonicalize to the same `https://github.com/<owner>/<repo>.git` URL.

## Input shapes

```bash
# Shorthand
agentskills-cli add owner/repo
agentskills-cli add owner/repo/skills/my-skill                 # with subpath
agentskills-cli add owner/repo#main                            # with branch/tag
agentskills-cli add owner/repo@my-skill                        # with single-skill filter
agentskills-cli add owner/repo#main@my-skill                   # both

# Full URL
agentskills-cli add https://github.com/owner/repo
agentskills-cli add https://github.com/owner/repo.git
agentskills-cli add https://github.com/owner/repo/tree/main/skills/my-skill

# Explicit prefix
agentskills-cli add github:owner/repo
```

## Detection rule

A source is treated as GitHub if it:

- matches the `owner/repo[/subpath]` regex AND doesn't contain `:` AND doesn't start with `.` or `/`, OR
- is a URL whose hostname is `github.com`, OR
- starts with `github:`.

## Fragment refs

The fragment after `#` becomes the git ref (branch/tag/commit) used when cloning. After `@` (in the fragment), the skill filter.

```bash
agentskills-cli add owner/repo#v1.2.0           # checks out v1.2.0
agentskills-cli add owner/repo#main@only-this   # main branch, only the "only-this" skill
```

## Auth

GitHub clones use your local `git` binary, which means **whatever git auth you've already set up just works**:

- SSH keys (`git@github.com:owner/repo.git`)
- HTTPS with git-credential-manager
- GitHub CLI's stored token

For public repos, no auth is needed.

## What happens under the hood

1. `git clone --depth 1` into a temp directory (with `--branch <ref>` if specified)
2. `GIT_TERMINAL_PROMPT=0` and `GIT_LFS_SKIP_SMUDGE=1` to avoid blocking on credentials or LFS
3. Discovery + install + lock-file update from the cloned content
4. Temp dir cleaned up after install

The clone timeout defaults to 5 minutes; override with `SKILLS_CLONE_TIMEOUT_MS`.

## Why GitHub is special vs other git sources

For GitHub specifically, [`add`](/commands/add) also records the git tree SHA of the installed skill's folder (`skillFolderHash`) into the lock file. This is what powers [`update`](/commands/update) - it can call the GitHub Trees API to detect remote changes without cloning.

[GitLab](/sources/gitlab) and [arbitrary git URLs](/sources/git) install the same way but don't get this update-detection automation in v1.
