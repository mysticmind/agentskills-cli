# npm

Install skills from any npm-compatible registry - public npmjs.org or private (Verdaccio, GitHub Packages npm, Sonatype Nexus, your own).

## Input shapes

```bash
# Scoped (auto-detected from leading @)
agentskills-cli add @my-org/agent-skills
agentskills-cli add @my-org/agent-skills@1.2.3        # pinned version
agentskills-cli add @my-org/agent-skills@next         # dist-tag

# Unscoped (requires the npm: prefix to disambiguate from NuGet)
agentskills-cli add npm:sample-pkg
agentskills-cli add npm:sample-pkg@1.3.0
```

## Detection rule

A source is treated as npm if it:

- starts with `npm:`, OR
- is a valid scoped package name (`@scope/name`).

Unscoped bare names (e.g., `lodash.merge`) hit the NuGet shorthand first, so unscoped npm packages need the `npm:` prefix.

## Registry resolution

AgentSkills CLI reads `.npmrc` files in this order (later wins):

1. `~/.npmrc`
2. Project `./.npmrc`

The default registry comes from the `registry=` setting (falls back to `https://registry.npmjs.org/`). Per-scope overrides are honored:

```
registry=https://registry.npmjs.org/
@my-org:registry=https://npm.contoso.com/team/
```

A scoped package `@my-org/foo` would be fetched from `https://npm.contoso.com/team/` in this config; everything else from npmjs.org.

Override the default registry for a single command with `--npm-registry <URL>`:

```bash
agentskills-cli add @my-org/foo --npm-registry https://npm.contoso.com/team/ -y
```

The override doesn't override per-scope rules in your `.npmrc` - the resolved registry for the specific scope still applies.

## Auth

Bearer tokens (`_authToken`), basic auth (`_auth`), and username/password forms are all read from `.npmrc`:

```
//npm.contoso.com/team/:_authToken=${MY_TOKEN}
//other.registry.com/:_auth=base64(user:pass)
```

`${ENV_VAR}` expansion happens at load time, just like npm. The token is matched against the registry URL by **longest path prefix** - so a token under `//npm.contoso.com/team/` is used for the `team/` registry but not for `//npm.contoso.com/other/`.

If `npm install <pkg>` works in your shell, `agentskills-cli add <pkg>` works the same way. Zero new auth surface.

## Package layout

The npm tarball roots at `package/`. The conventional layout:

```
mypackage.tgz
└─ package/
   ├─ package.json
   └─ skills/
      └─ <skill-name>/SKILL.md
```

Probed in order: `package/skills/`, `package/contentFiles/any/any/skills/`, then a recursive scan as fallback.

## pnpm / bun / yarn

The `.npmrc` format is shared:

| Tool | Reads `.npmrc`? | Works? |
|---|---|---|
| npm | yes | yes |
| pnpm | yes | yes (same config) |
| bun | yes | yes (same config) |
| yarn 1 | prefers `.yarnrc` over `.npmrc` | partial - if your auth is only in `.yarnrc`, AgentSkills CLI can't see it. Workaround: also mirror to `.npmrc`, or use `--npm-registry` |
| yarn 2+ | uses `.yarnrc.yml` | same as yarn 1 |

AgentSkills CLI doesn't shell out to any of these - it talks to the npm registry HTTP API directly. **You do not need `node` or any JS tool installed** for the npm source to work.

## Authoring npm packages with skills

See [Publishing to npm](/authoring/publishing-npm) for the full guide, including local Verdaccio testing without publishing to npmjs.org.
