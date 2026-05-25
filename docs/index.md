---
layout: home

hero:
  name: AgentSkills CLI
  text: The .NET commandline tool for the Agent Skills ecosystem.
  tagline: Install skills from GitHub, NuGet, npm, well-known endpoints, or local folders into Claude Code, Cursor, Codex, OpenCode, and the universal .agents/skills directory. First-class NuGet and npm. Library-bundled skill packages. Extension points by design.
  image:
    light: /logo.png
    dark: /logo-dark.png
    alt: AgentSkills CLI hexagonal terminal logo
  actions:
    - theme: brand
      text: Get started
      link: /getting-started/install
    - theme: alt
      text: Why AgentSkills CLI?
      link: /why
    - theme: alt
      text: View on GitHub
      link: https://github.com/mysticmind/agentskills-cli

features:
  - title: First-class NuGet support
    details: Install from public or private NuGet feeds using your existing NuGet.config and credential providers. Azure Artifacts, GitHub Packages, ProGet, MyGet - if `dotnet restore` works against your feed, `agentskills-cli` does too. No new auth surface.
  - title: Ship skills inside your library
    details: Drop a skills/ folder into your existing NuGet or npm package and it becomes a skill source automatically. The SDK and the agent guidance for using it travel together, version together, and discover together. Library authors keep doing what they already do.
  - title: First-class npm registry fetch
    details: Public and private npm registries via your existing .npmrc - scoped registries, _authToken, longest-path-prefix auth matching. Real registry fetch, not just node_modules sync. No Node runtime needed.
  - title: Every coding agent
    details: Claude Code, Cursor, Codex, OpenCode, and any agent that reads the universal .agents/skills directory. Auto-detected; install once, picked up by all of them.
  - title: Extension points by design
    details: Register ISkillSourceFactory for new source types (cargo, oci, conda, your internal feed) and ISkillSearchProvider for new search backends (internal registries, GitHub topic search) with a single DI registration. No core changes.
  - title: Built on the open spec
    details: Implements the SKILL.md format and the well-known discovery endpoint from agentskills.io. Lock-file interop with the broader ecosystem - the same skill installed by any spec-compliant tool is visible to all of them.
---
