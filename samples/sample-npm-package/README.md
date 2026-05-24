# @acme/agent-skills-sample

A trivial npm package showing the AgentSkills layout for shipping skills via the npm registry. The layout:

```
@acme/agent-skills-sample/
├── package.json
├── README.md
└── skills/
    └── sample-npm-greeter/
        └── SKILL.md
```

When packed, the tarball roots at `package/`, so the on-the-wire structure is `package/skills/sample-npm-greeter/SKILL.md` - exactly what `AgentSkills` looks for first when extracting an npm source.

## Test it locally without publishing

### Option A - inspect the tarball

```bash
cd samples/sample-npm-package
npm pack                                                # → @acme/agent-skills-sample-0.1.0.tgz
tar -tzf acme-agent-skills-sample-0.1.0.tgz             # confirm package/skills/sample-npm-greeter/SKILL.md is in there
```

### Option B - publish to a local Verdaccio and install

```bash
# 1. Start a local registry (one-time, in another terminal)
npx verdaccio                                            # listens on http://localhost:4873

# 2. Publish the sample to it
cd samples/sample-npm-package
npm publish --registry http://localhost:4873            # one-time auth prompt the first time

# 3. Install with AgentSkills, pointed at the local registry
cd /tmp/test-project
agentskills add @acme/agent-skills-sample \
  --npm-registry http://localhost:4873 \
  -a universal -y --copy

# 4. Verify
ls .agents/skills/sample-npm-greeter/SKILL.md
```

## Publish to npmjs.org (real package)

```bash
cd samples/sample-npm-package
npm publish --access public                              # scoped packages need explicit public
```

Users then install with:

```bash
agentskills add @acme/agent-skills-sample -y
```

## Auth for private registries

Auth is read from `~/.npmrc` (and any project `.npmrc`) - the same files `npm install` already uses. No new auth surface. Example for a private feed:

```
@my-org:registry=https://npm.contoso.com/team/
//npm.contoso.com/team/:_authToken=${MY_TOKEN}
```

Then:

```bash
MY_TOKEN=… agentskills add @my-org/agent-skills -y
```

Per-scope registry rules, longest-path-prefix token matching, and `${ENV_VAR}` expansion all work the same as in `npm`.
