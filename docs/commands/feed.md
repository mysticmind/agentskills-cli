# `feed`

Manage the NuGet feeds used for skills. Feeds are stored in `.agents/nuget.config` (project, committed) or `~/.agents/nuget.config` (yours, with `-g`), separate from your normal NuGet configuration. See [Private feeds and registries](/sources/private-feeds) for how the files combine.

## `feed add`

```
agentskills-cli feed add <url-or-folder> [--name <name>] [-g]
                         [--username <user> --password <password> [--store-password-in-clear-text]]
```

| Option | Meaning |
|---|---|
| `<url-or-folder>` | A feed URL (`https://.../v3/index.json`) or a local folder. A folder inside the project is stored relative to the config file, so it works on every clone. |
| `-n`, `--name` | Feed name. Defaults to the host name, or the folder name for a local feed. Adding a name that already exists updates it. |
| `-g`, `--global` | Write to `~/.agents/nuget.config` instead of the project file. |
| `-u`, `--username`, `--password` | Credentials. Only allowed with `-g`: the project file is meant to be committed. |
| `--store-password-in-clear-text` | Store the password unencrypted. Required outside Windows, where NuGet can't encrypt it. |

## `feed list`

```
agentskills-cli feed list [--json]
```

Shows every NuGet feed in effect, in the order the tool tries them, with whether it's enabled, whether stored credentials exist, and which file defines it. Credentials supplied by a credential provider aren't shown. When [package source mapping](/sources/private-feeds#package-source-mapping) is configured, a **Packages** column shows which package patterns each feed serves.

## `feed remove`

```
agentskills-cli feed remove <name> [-g]
```

Removes a feed, and its credentials, from the project file (or the user file with `-g`). If the feed is defined somewhere else, the message says where.

## Using a feed for one command

`--nuget-source` on `add`, `update`, `outdated` and `restore` accepts a URL, a folder, or the name of a configured feed. A configured feed keeps its credentials:

```bash
agentskills-cli add Contoso.AgentSkills --nuget-source contoso -y
```
