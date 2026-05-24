---
layout: home

hero:
  name: AgentSkills
  text: Install agent skills from anywhere into every coding agent.
  tagline: A .NET CLI that pulls skills from GitHub, NuGet, npm, well-known endpoints, or local folders and drops them into Claude Code, Cursor, Codex, OpenCode, and the universal .agents/skills directory. One command, every agent, no JavaScript runtime required.
  image:
    light: /logo.png
    dark: /logo-dark.png
    alt: AgentSkills hexagonal terminal logo
  actions:
    - theme: brand
      text: Get started
      link: /getting-started/install
    - theme: alt
      text: Why AgentSkills?
      link: /why
    - theme: alt
      text: View on GitHub
      link: https://github.com/mysticmind/agentskills-cli

features:
  - title: Install from anywhere
    details: GitHub, GitLab, any git URL, NuGet packages (public and private), npm packages (public and private), well-known endpoints, or local folders. One CLI, one mental model.
  - title: Ship skills inside your library
    details: Drop a skills/ folder into your existing NuGet or npm package. The SDK and the agent guidance for using it travel together, version together, and discover together.
  - title: Every coding agent
    details: Claude Code, Cursor, Codex, OpenCode, and any agent that reads the universal .agents/skills directory. Auto-detected; install once, picked up by all of them.
  - title: Runs as dnx
    details: One-shot via dnx agentskills, no install step required on .NET 10. Or dotnet tool install --global agentskills for daily use on .NET 8 or 10.
  - title: Extension points by design
    details: Custom source types via ISkillSourceFactory. Custom search backends via ISkillSearchProvider. Add new ecosystems with a single DI registration.
  - title: Built on the open spec
    details: Implements the SKILL.md format and the well-known discovery endpoint from agentskills.io. Skills published for npx skills work here, and vice versa.
---
