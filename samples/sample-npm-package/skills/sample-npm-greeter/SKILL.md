---
name: sample-npm-greeter
description: Sample skill packaged in an npm tarball for AgentSkills smoke tests.
---
# sample-npm-greeter

This skill was installed from an npm package via `agentskills add @acme/agent-skills-sample`.

`AgentSkills` looks for `package/skills/<name>/SKILL.md` inside the tarball first (the convention this sample follows), then `package/contentFiles/any/any/skills/`, then falls back to a recursive scan.
