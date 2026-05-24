import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'AgentSkills',
  description:
    'Install agent skills from GitHub, NuGet, npm, or local folders into Claude Code, Cursor, Codex & friends. The .NET CLI for the open Agent Skills ecosystem.',

  // GitHub Pages project site lives at mysticmind.github.io/agentskills-cli/.
  // `base` makes every asset and internal link resolve under that subpath.
  base: '/agentskills-cli/',

  cleanUrls: true,
  lastUpdated: true,

  head: [
    ['meta', { name: 'theme-color', content: '#0EA5E9' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'AgentSkills' }],
    [
      'meta',
      {
        property: 'og:description',
        content:
          'Install agent skills from GitHub, NuGet, npm, or local folders. The .NET CLI for the open Agent Skills ecosystem.',
      },
    ],
  ],

  themeConfig: {
    siteTitle: 'AgentSkills',

    nav: [
      { text: 'Guide', link: '/getting-started/install', activeMatch: '^/(?!$)' },
    ],

    // One unified sidebar shown on every doc page. Groups start expanded; set
    // `collapsed: true` on any group below to start it folded instead.
    sidebar: [
      {
        text: 'Getting started',
        items: [
          { text: 'Why AgentSkills?', link: '/why' },
          { text: 'Install', link: '/getting-started/install' },
          { text: 'Concepts', link: '/getting-started/concepts' },
          { text: 'Quick start', link: '/getting-started/quick-start' },
        ],
      },
      {
        text: 'Commands',
        collapsed: false,
        items: [
          { text: 'add', link: '/commands/add' },
          { text: 'list', link: '/commands/list' },
          { text: 'remove', link: '/commands/remove' },
          { text: 'init', link: '/commands/init' },
          { text: 'find', link: '/commands/find' },
          { text: 'update', link: '/commands/update' },
        ],
      },
      {
        text: 'Source formats',
        collapsed: true,
        items: [
          { text: 'Overview', link: '/sources/' },
          { text: 'Local folders', link: '/sources/local' },
          { text: 'GitHub', link: '/sources/github' },
          { text: 'GitLab', link: '/sources/gitlab' },
          { text: 'Arbitrary git URL', link: '/sources/git' },
          { text: 'NuGet', link: '/sources/nuget' },
          { text: 'npm', link: '/sources/npm' },
          { text: 'Well-known endpoints', link: '/sources/well-known' },
        ],
      },
      {
        text: 'Authoring skills',
        collapsed: true,
        items: [
          { text: 'SKILL.md format', link: '/authoring/skill-format' },
          { text: 'Publishing to NuGet', link: '/authoring/publishing-nuget' },
          { text: 'Publishing to npm', link: '/authoring/publishing-npm' },
        ],
      },
      {
        text: 'Reference',
        collapsed: true,
        items: [
          { text: 'Supported agents', link: '/reference/agents' },
          { text: 'Where files land', link: '/reference/where-files-land' },
          { text: 'Lock files', link: '/reference/lock-files' },
          { text: 'Environment variables', link: '/reference/environment-variables' },
          { text: 'Search providers (extension point)', link: '/reference/search-providers' },
          { text: 'Samples', link: '/reference/samples' },
          { text: 'AgentSkills vs npx skills', link: '/reference/comparison' },
        ],
      },
      {
        text: 'Tutorials',
        collapsed: true,
        items: [
          { text: 'Common workflows', link: '/tutorials/common-workflows' },
          { text: 'Ship skills with your library', link: '/tutorials/ship-skills-with-library' },
        ],
      },
      {
        text: 'Help',
        collapsed: true,
        items: [
          { text: 'Troubleshooting', link: '/troubleshooting' },
          { text: 'FAQ', link: '/faq' },
        ],
      },
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/mysticmind/agentskills-cli' },
    ],

    editLink: {
      pattern: 'https://github.com/mysticmind/agentskills-cli/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2026 Babu Annamalai',
    },

    search: { provider: 'local' },

    outline: { level: [2, 3] },
  },
})
