# Common workflows

Short, copy-pastable recipes for the patterns that come up most often. Each is self-contained - run top to bottom in a fresh shell.

## Bootstrap a fresh project with a curated skill set

Pin an entire NuGet (or npm) skill collection at the project level, commit the lock, and your teammates reproduce on `agentskills-cli add` against the same source.

```bash
cd my-app
agentskills-cli add MyOrg.CuratedSkills -y
git add .agents/ skills-lock.json
git commit -m "chore: pin agent skills"
```

Teammates run `agentskills-cli add <same source> -y` to land the same skills. See [Lock files](/reference/lock-files) for the project-lock schema.

## Add a single skill globally so every project has it

`-g` writes to the global lock under `~/.agents/.skill-lock.json` and installs into the per-agent global directories.

```bash
agentskills-cli add vercel-labs/agent-skills -g -s web-design-guidelines -y
```

After this, every project on the machine that has Claude Code (or Cursor, Codex, etc.) installed picks up `web-design-guidelines` without any per-project install step.

## Try everything in a NuGet package, then prune

Useful when evaluating a community skill pack: install everything, see what's there, drop what you don't need.

```bash
agentskills-cli add MyOrg.AgentSkills -y                # installs all skills in the package
agentskills-cli list MyOrg.AgentSkills --paths          # what landed, with on-disk paths
agentskills-cli remove unused-skill -y                  # drop one specific skill
agentskills-cli remove MyOrg.AgentSkills -y             # ...or roll the whole package back
```

The `--by package` flag on [`list`](/commands/list) groups output by source package, which makes "what came from where" obvious when you have skills from multiple packages installed.

## Drive `dnx` from CI without ever installing the tool

`dnx` resolves and runs the latest `agentskills-cli` from your configured NuGet feeds in one shot - no `dotnet tool install` step. Perfect for ephemeral CI runners.

```bash
dnx agentskills-cli -y -- add ./my-skill -a claude-code -y --copy
```

The `-y --` before `add` tells `dnx` to skip its own confirmation prompt and pass everything after the `--` as arguments to the tool.

For a CI run that publishes built artifacts to a local feed first and then verifies them:

```bash
dotnet pack src/MyOrg.AgentSkills -o ./local-feed
dnx agentskills-cli -y -- add MyOrg.AgentSkills \
  --nuget-source ./local-feed -a universal -y --copy
```

Requires `.NET 10` SDK on the runner (`dnx` ships with .NET 10). For .NET 8-only runners, fall back to `dotnet tool install --global agentskills-cli` once at the top of the job.

## Refresh everything from source

The [`update`](/commands/update) command queries the GitHub Trees API for every tracked GitHub source and reinstalls any that have drifted. Always preview with `--check` first.

```bash
agentskills-cli update --check                          # dry-run table
agentskills-cli update -g -y                            # actually update globals
agentskills-cli update -y                               # update the current project
```

Sources that can't be checked automatically (local paths, generic git URLs, GitLab, NuGet, npm) are listed as skipped in the output. See [`update`](/commands/update) for the full behavior and GitHub auth chain.

## Set up a private NuGet feed for team skills

If your team publishes skills to an internal feed (Azure Artifacts, GitHub Packages, ProGet, MyGet, etc.), AgentSkills CLI reads your existing `NuGet.config` and credential providers automatically - no new auth surface.

```bash
# One-time, machine-wide:
dotnet nuget add source https://pkgs.contoso.com/v3/index.json \
  -n contoso -u $USER -p $TOKEN --store-password-in-clear-text

# Daily use - no auth flags needed, credentials picked up from NuGet.config:
agentskills-cli add Contoso.AgentSkills -y
```

For Azure Artifacts specifically, install the [Azure Artifacts Credential Provider](https://github.com/microsoft/artifacts-credprovider) once and `dotnet`/`agentskills-cli` auth seamlessly.

## Mirror the install on a teammate's box

Clone the project, run one command, your teammate has the same skills.

```bash
git clone <project repo> && cd <project>
# If the project lock pins to public sources, this just works:
agentskills-cli add $(cat skills-lock.json | jq -r '.skills | to_entries[] | .value.source')
```

A `agentskills-cli install --from-lock` command is on the roadmap; for now the loop above (or a tiny script) covers it.

## Next

- [Ship skills with your library](/tutorials/ship-skills-with-library) - the long-form tutorial
- [`add`](/commands/add), [`list`](/commands/list), [`remove`](/commands/remove), [`update`](/commands/update) - full command reference
- [Source formats](/sources/) - every supported source type
