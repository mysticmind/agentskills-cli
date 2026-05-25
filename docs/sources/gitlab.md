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

## `update` limitation

GitLab tree-SHA detection isn't implemented in v1; [`update`](/commands/update) reports GitLab sources as **Skipped** with the reason `GitLab - Trees API not implemented in v1`. To refresh a GitLab-sourced skill, re-run `add` manually.
