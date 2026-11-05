# GitLab

Behaves like [GitHub](/sources/github) - same `git clone --depth 1` mechanic, same auth story (inherits your local git config). Subgroups are fully supported.

## Input shapes

```bash
# Prefix shorthand
agentskills-cli add gitlab:group/repo

# Full URL - simple
agentskills-cli add https://gitlab.com/group/repo

# Full URL - with subgroup
agentskills-cli add https://gitlab.com/group/subgroup/repo

# Full URL - with /-/tree/<ref>/<path>
agentskills-cli add https://gitlab.com/group/repo/-/tree/main/skills/my-skill
```

## Detection rule

A source is treated as GitLab if it's a URL with hostname `gitlab.com` or its path matches the GitLab tree pattern (`/-/tree/<ref>`), or it starts with `gitlab:`.

## Self-hosted GitLab

Self-hosted GitLab URLs (e.g., `https://git.contoso.com/group/repo`) are treated as [arbitrary git URLs](/sources/git) by default - they still clone correctly, just without GitLab-specific URL parsing for subpaths. If you have a use case for first-class self-hosted GitLab detection, open an issue.

## `update`

[`update`](/commands/update) and [`outdated`](/commands/outdated) shallow-clone the recorded repo and ref and compare the skill folder with the baseline recorded at install. Skills installed globally before 1.1.0 have no baseline; refresh them once with `update --force`.
