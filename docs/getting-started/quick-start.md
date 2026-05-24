# Quick start

A five-minute tour. Assumes you've [installed](/getting-started/install) the global tool.

## At a glance

What the CLI looks like end-to-end. Each section below covers one of these in detail.

```bash
# Install from any source - one command, several ecosystems
agentskills add anthropics/skills                      # GitHub shorthand
agentskills add MyOrg.AgentSkills                      # NuGet package
agentskills add @my-org/agent-skills                   # npm package
agentskills add ./my-local-skill                       # local folder

# Inspect what is installed, grouped however you want
agentskills list --by package

# Search community skills
agentskills find testing

# Remove an entire package's worth of skills
agentskills remove @my-org/agent-skills -y
```

Now the step-by-step.

## 1. Scaffold a skill

```bash
mkdir my-first-skill && cd my-first-skill
agentskills init . -y
```

Open `SKILL.md`. The scaffolded file looks like this:

```markdown
---
name: my-first-skill
description: One-line description that helps an agent decide when to use this skill.
---
# my-first-skill

What this skill does and how it should be used by an agent.

## When to invoke

- Bullet trigger conditions

## Notes

- Anything else the agent should know.
```

Edit the `description` and body to describe whatever you want an AI agent to do.

## 2. Install it

```bash
cd ..
agentskills add ./my-first-skill -a universal -y --copy
```

The output shows where the files landed:

```
╭───────────────────┬───────────┬────────┬────────────────────────────────────╮
│ Skill             │ Agent     │ Result │ Path                               │
├───────────────────┼───────────┼────────┼────────────────────────────────────┤
│ my-first-skill    │ Universal │ copied │ /path/to/cwd/.agents/skills/my-…   │
╰───────────────────┴───────────┴────────┴────────────────────────────────────╯
Installed under /path/to/cwd/.agents/skills
Done.
```

A `skills-lock.json` is written alongside, recording what was installed and from where.

## 3. List what's installed

```bash
agentskills list
```

Group by package or by install path:

```bash
agentskills list --by package
agentskills list --by path --paths
```

## 4. Install from a real source

Pick whichever ecosystem fits:

::: code-group

```bash [GitHub]
agentskills add anthropics/skills -y --copy
```

```bash [NuGet]
agentskills add MyOrg.AgentSkills -y --copy
```

```bash [npm]
agentskills add @my-org/agent-skills -y --copy
```

```bash [Local]
agentskills add /path/to/skill -y --copy
```

:::

All four use the same install pipeline. The result table tells you exactly where the files landed and the lock file records the source for `agentskills update` later.

## 5. Search the community registry

```bash
agentskills find testing
```

Picks an entry, hands it off to the install flow. See [`find`](/commands/find) for the full UX.

## 6. Remove cleanly

```bash
agentskills remove my-first-skill -y
```

Or remove every skill that came from a specific source (the [unified-targets behavior](/commands/remove)):

```bash
agentskills remove MyOrg.AgentSkills -y       # all skills from that NuGet package
agentskills remove anthropics/skills -y       # all skills from that GitHub repo
```

## What next

- **[Commands](/commands/add)** for the full flag reference on each subcommand
- **[Source formats](/sources/)** for every input shape AgentSkills accepts
- **[Ship skills with your library](/tutorials/ship-skills-with-library)** if you maintain a NuGet or npm package and want users to get agent guidance "for free" when they install your SDK
