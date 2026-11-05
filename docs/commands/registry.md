# `registry`

Manage the npm registries used for skills. Settings are stored in `.agents/.npmrc` (project, committed) or `~/.agents/.npmrc` (yours, with `-g`), separate from your normal `.npmrc`. See [Private feeds and registries](/sources/private-feeds) for how the files combine.

## `registry add`

```
agentskills-cli registry add <url> [--scope <@scope>] [-g] [--token-env <VAR> | --token <token>]
```

| Option | Meaning |
|---|---|
| `<url>` | The registry URL. |
| `--scope` | Use this registry only for one scope, e.g. `@contoso`. Without it, this becomes the default registry for skills. |
| `-g`, `--global` | Write to `~/.agents/.npmrc` instead of the project file. |
| `--token-env <VAR>` | Write `${VAR}` as the auth token, so the token is read from that environment variable when the tool runs. Allowed in the project file; the right choice for CI. |
| `--token <token>` | Store the token itself. Only allowed with `-g`. The file is made readable only by you on Linux and macOS. |

```bash
agentskills-cli registry add https://npm.contoso.com/ --scope @contoso --token-env CONTOSO_NPM_TOKEN
```

writes this to `.agents/.npmrc`:

```ini
@contoso:registry=https://npm.contoso.com/
//npm.contoso.com/:_authToken=${CONTOSO_NPM_TOKEN}
```

## `registry list`

```
agentskills-cli registry list [--json]
```

Shows the default registry and every scoped registry in effect, whether auth is configured for it, and which file defines it.

## `registry remove`

```
agentskills-cli registry remove <url | @scope | default> [-g]
```

Removes a scoped registry, the default registry, or every entry pointing at a URL from the project file (or the user file with `-g`), along with that registry's auth settings when nothing else in the file still uses it.
