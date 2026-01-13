import { defineConfig } from 'vitepress'

export default defineConfig({
  srcDir: "content",
  
  title: "2dog - old robot, new tricks",
  description: "Embed Godot Engine in your .NET applications",
  themeConfig: {
    siteTitle: false,
    logo: '/icon.svg',
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Guide', link: '/getting-started' },
      { text: 'API', link: '/api-reference' }
    ],

    sidebar: [
      {
        text: 'Guide',
        items: [
          { text: 'Getting Started', link: '/getting-started' },
          { text: 'Core Concepts', link: '/concepts' },
          { text: 'Configuration', link: '/configuration' }
        ]
      },
      {
        text: 'Reference',
        items: [
          { text: 'API Reference', link: '/api-reference' },
          { text: 'Testing with xUnit', link: '/testing' }
        ]
      }
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/outfox/2dog' },
      { icon: 'discord', link: 'https://discord.gg/GAXdbZCNGT' }
    ]
  }
})
