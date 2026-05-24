# Arbitrary git URL

For self-hosted git hosts (Gitea, Forgejo, Bitbucket Server, custom GitLab instances, etc.) that aren't auto-detected as GitHub or GitLab. Behavior is identical: `git clone --depth 1`, your local git auth.

## Input shapes

```bash
# HTTPS
agentskills add https://git.contoso.com/team/skills.git
agentskills add https://gitea.example.com/owner/repo.git

# SSH
agentskills add git@git.contoso.com:team/skills.git
agentskills add ssh://git@host.example.com/team/skills.git
```

## Detection rule

Any URL ending in `.git` (or any non-GitHub/non-GitLab git URL) falls into this bucket as the fallback after more specific rules don't match.

## Auth

Same as GitHub/GitLab: whatever your local `git` knows about. SSH keys, credential manager, etc.

## Subpath / ref support

The arbitrary-git source doesn't parse subpath / ref from the URL. If you need those, use the [`--path`](/commands/add) flag at install time and rely on the cloned repo's default branch:

```bash
agentskills add https://git.contoso.com/team/skills.git --path some/subfolder -y
```

For pinning a specific branch on a generic git URL, the simplest workaround is to publish that branch as a tag and pass the tag via the fragment - which works for any git-shaped source.

## `update`

Same as [GitLab](/sources/gitlab): no automated drift detection in v1; reported as **Skipped** with reason `Generic git URL - re-install manually`. Re-run `add` to refresh.
