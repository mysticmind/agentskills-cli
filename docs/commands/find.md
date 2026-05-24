# `find`

Search registered providers for skills. Out of the box that's [skills.sh](https://skills.sh); add more by registering an [`ISkillSearchProvider`](/reference/search-providers).

## Synopsis

```
agentskills find [QUERY] [-g] [-y] [--provider NAME...] [--list-providers]
```

## Arguments

| Argument | Meaning |
|---|---|
| `QUERY` | Search terms. Omit in an interactive shell to be prompted. |

## Flags

| Flag | Meaning |
|---|---|
| `-g`, `--global` | When picking a result, install it globally. |
| `-y`, `--yes` | Non-interactive output only: print the result table and exit (no install prompt). |
| `--provider <NAME>` | Restrict the fan-out to specific providers. Repeatable; the search runs against the union. Unknown name errors with the available-providers list. Omit to query every enabled provider. |
| `--list-providers` | Print the registered providers (name, enabled/disabled, implementing type) and exit. |

## Behavior

By default `find` fans out across every enabled provider in parallel, applies a 10-second timeout to each, dedupes results by `(source, skill name)`, and sorts by installs. A failing or slow provider is dropped from that run - the rest still render. Each result row is tagged with its origin in a `Provider` column.

In an interactive shell, results render as a table and then a `SelectionPrompt` lets you pick one to install via the regular [`add`](/commands/add) pipeline.

## Examples

```bash
# All enabled providers (default)
agentskills find testing

# One specific provider
agentskills find testing --provider skills.sh

# Union of two named providers
agentskills find testing --provider skills.sh --provider contoso

# What's wired up?
agentskills find --list-providers
```

`--list-providers` output:

```
╭───────────┬─────────┬────────────────────────────────────────────╮
│ Provider  │ Status  │ Implementation                             │
├───────────┼─────────┼────────────────────────────────────────────┤
│ skills.sh │ enabled │ AgentSkills.Sources.SkillsShSearchProvider │
╰───────────┴─────────┴────────────────────────────────────────────╯
```

## The wider story

The search backend is an extension point, not a fixed dependency on skills.sh. Once an organization has its own internal skill registry, registering `ISkillSearchProvider` makes `agentskills find` query both sources at once. See [Search providers](/reference/search-providers) for the contract.
