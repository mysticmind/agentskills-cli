# Publishing skills as an npm package

Same dual-mode story as NuGet: ship a **skills-only package** when there's no library to attach to, or **add a `skills/` directory to your existing library package** so the SDK and its agent guidance travel as one artifact.

A `@my-org/sample-toolkit-skills` package is fine; adding `skills/` to `@my-org/sample-toolkit` is often better - users searching for the toolkit on npmjs.org get the skills for free, and the skills version-lock with the toolkit.

## The convention

```
@my-org/agent-skills/
├── package.json                              # scoped names auto-detect as npm
├── README.md
└── skills/
    ├── skill-one/SKILL.md
    └── skill-two/SKILL.md
```

When packed, the tarball roots at `package/`, so the on-the-wire structure is `package/skills/<name>/SKILL.md` - exactly what AgentSkills CLI looks for first when extracting an npm source.

## Minimum `package.json`

```json
{
  "name": "@my-org/agent-skills",
  "version": "0.1.0",
  "description": "Our team's curated agent skills.",
  "license": "MIT",
  "files": ["skills/", "README.md"],
  "keywords": ["agent-skills", "skills"]
}
```

The `files` array controls what `npm pack` includes. Anything outside it is excluded from the tarball.

## Pack and publish

```bash
npm pack                                                    # → @my-org-agent-skills-0.1.0.tgz
npm publish --access public                                 # public; drop --access for private/scoped
```

Auth comes from your normal `~/.npmrc` (the same one `npm install` uses).

Users install with:

```bash
agentskills-cli add @my-org/agent-skills -y                     # scoped → auto-detected as npm
agentskills-cli add npm:unscoped-pkg -y                         # unscoped requires explicit npm: prefix
```

## Bundle into an existing library package

If you maintain a TypeScript/JavaScript library, adding skills is one folder + one line in `package.json`.

Existing structure:

```
@my-org/sample-toolkit/
├── package.json
├── README.md
├── dist/         # your compiled output
└── src/          # source
```

Add a top-level `skills/` directory and update `files`:

```diff
{
  "name": "@my-org/sample-toolkit",
  "version": "2.5.0",
  ...
- "files": ["dist/", "README.md"]
+ "files": ["dist/", "README.md", "skills/"]
}
```

Now `npm pack` includes the skills in `package/skills/...`, and any user installing `@my-org/sample-toolkit` from npm has them available to AgentSkills CLI automatically.

## Local testing with Verdaccio

You don't need to push to npmjs.org to test the publish + install flow. [Verdaccio](https://verdaccio.org/) runs a local npm registry in seconds:

```bash
# In one terminal
npx verdaccio                                            # listens on http://localhost:4873

# In your package dir
npm publish --registry http://localhost:4873            # one-time auth prompt the first time

# In any test project
agentskills-cli add @my-org/agent-skills \
  --npm-registry http://localhost:4873 \
  -a universal -y --copy
```

This is the recommended way to validate a publish before committing to npmjs.org.

## Auth for private registries

Auth is read from `~/.npmrc` and project `.npmrc` - same files `npm install` already uses. No new auth surface. Example for a private feed:

```
@my-org:registry=https://npm.contoso.com/team/
//npm.contoso.com/team/:_authToken=${MY_TOKEN}
```

Then:

```bash
MY_TOKEN=… agentskills-cli add @my-org/agent-skills -y
```

Per-scope registry rules, longest-path-prefix token matching, and `${ENV_VAR}` expansion all work the same as in `npm`.

## Prefer the convention

`package/skills/<name>/SKILL.md` is auto-discovered with no extra flags. AgentSkills CLI also falls back to a recursive scan, and users can install with `agentskills-cli add @your-org/your-package --path src/skills` when skills live elsewhere, but **every non-conventional layout is friction the user has to learn**. Use the convention unless you have a hard reason not to.

## See the working sample

A complete example lives at [`samples/sample-npm-package`](https://github.com/mysticmind/agentskills-cli/tree/main/samples/sample-npm-package).
