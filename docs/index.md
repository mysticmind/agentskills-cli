---
layout: home

hero:
  name: AgentSkills CLI
  text: The .NET commandline tool for the Agent Skills ecosystem.
  tagline: Install skills from any source into any coding agent. One command, every ecosystem.
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
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m7.5 4.27 9 5.15"/><path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/></svg>'
    title: First-class NuGet support
    details: Install from public or private NuGet feeds using your existing NuGet.config and credential providers. Azure Artifacts, GitHub Packages, ProGet, MyGet - if `dotnet restore` works against your feed, `agentskills-cli` does too. No new auth surface.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m16 6 4 14"/><path d="M12 6v14"/><path d="M8 8v12"/><path d="M4 4v16"/></svg>'
    title: Ship skills inside your library
    details: Drop a skills/ folder into your existing NuGet or npm package and it becomes a skill source automatically. The SDK and the agent guidance for using it travel together, version together, and discover together. Library authors keep doing what they already do.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 9h18v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V9Z"/><path d="m3 9 2.45-4.9A2 2 0 0 1 7.24 3h9.52a2 2 0 0 1 1.8 1.1L21 9"/><path d="M12 3v6"/></svg>'
    title: First-class npm registry fetch
    details: Public and private npm registries via your existing .npmrc - scoped registries, _authToken, longest-path-prefix auth matching. Real registry fetch, not just node_modules sync. No Node runtime needed.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 8V4H8"/><rect width="16" height="12" x="4" y="8" rx="2"/><path d="M2 14h2"/><path d="M20 14h2"/><path d="M15 13v2"/><path d="M9 13v2"/></svg>'
    title: Every coding agent
    details: Claude Code, Cursor, Codex, OpenCode, and any agent that reads the universal .agents/skills directory. Auto-detected; install once, picked up by all of them.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15.39 4.39a1 1 0 0 0 1.68-.474 2.5 2.5 0 1 1 3.014 3.015 1 1 0 0 0-.474 1.68l1.683 1.682a2.414 2.414 0 0 1 0 3.414L19.61 15.39a1 1 0 0 1-1.68-.474 2.5 2.5 0 1 0-3.014 3.015 1 1 0 0 1 .474 1.68l-1.683 1.682a2.414 2.414 0 0 1-3.414 0L8.61 19.61a1 1 0 0 0-1.68.474 2.5 2.5 0 1 1-3.014-3.015 1 1 0 0 0 .474-1.68l-1.683-1.682a2.414 2.414 0 0 1 0-3.414L4.39 8.61a1 1 0 0 1 1.68.474 2.5 2.5 0 1 0 3.014-3.015 1 1 0 0 1-.474-1.68l1.683-1.682a2.414 2.414 0 0 1 3.414 0z"/></svg>'
    title: Extension points by design
    details: Register ISkillSourceFactory for new source types (cargo, oci, conda, your internal feed) and ISkillSearchProvider for new search backends (internal registries, GitHub topic search) with a single DI registration. No core changes.
  - icon: '<svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 7v14"/><path d="M16 12h2"/><path d="M16 8h2"/><path d="M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3z"/><path d="M6 12h2"/><path d="M6 8h2"/><path d="M21 18a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1h-5a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3z"/></svg>'
    title: Built on the open spec
    details: Implements the SKILL.md format and the well-known discovery endpoint from agentskills.io. Lock-file interop with the broader ecosystem - the same skill installed by any spec-compliant tool is visible to all of them.
---
