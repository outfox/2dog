import { defineConfig } from 'vitepress'

export default defineConfig({
  srcDir: "content",
  
  title: "2dog",
  titleTemplate: ":title – Godot in .NET",
  description: "Embed Godot Engine in your .NET applications",

  head: [
    ['link', { rel: 'preconnect', href: 'https://fonts.googleapis.com' }],
    ['link', { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' }],
    ['link', { rel: "apple-touch-icon", sizes: "180x180", href: "/apple-touch-icon.png" }],
    ['link', { rel: "icon", type: "image/png", sizes: "96x96", href: "/favicon-96x96.png" }],
    ['link', { rel: "icon", type: "image/png", sizes: "32x32", href: "/favicon-32x32.png" }],
    ['link', { rel: "icon", type: "image/png", sizes: "16x16", href: "/favicon-16x16.png" }],
    ['link', { rel: "icon", type: "image/svg+xml", href: "/favicon.svg" }],
    ['link', { rel: "icon", type: "image/svg+xml", href: "/favicon.ico" }],
    ['link', { rel: "manifest", href: "/site.webmanifest" }],
    ['meta', { name: "author", content: "outfox" }],
    ['meta', { name: "theme-color", content: "#478cbf" }],
    ['meta', { name: "twitter:card", content: "summary_large_image" }],
    ['meta', { name: "twitter:title", content: "2dog – Godot... backwards? Run your Godot projects from .NET!" }],
    ['meta', { name: "twitter:image", content: "https://2dog.dev/og-image.png" }],
    ['meta', { name: "twitter:image:alt", content: "2dog logo - a happy white dog" }],
    ['meta', { name: "twitter:description", content: "Free & Libre Open Source! Embed Godot Engine in your .NET applications. Full engine control, xUnit testing, and CI/CD support." }],
    ['meta', { property: "og:type", content: "website" }],
    ['meta', { property: "og:url", content: "https://2dog.dev" }],
    ['meta', { property: "og:title", content: "2dog – Godot... backwards? Run your Godot projects from .NET!" }],
    ['meta', { property: "og:description", content: "Free & Libre Open Source! Embed Godot Engine in your .NET applications. Full engine control, xUnit testing, and CI/CD support." }],
    ['meta', { property: "og:image", content: "https://2dog.dev/og-image.png" }],
  ],


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
      { icon: 'discord', link: 'https://discord.gg/GAXdbZCNGT' },
      { icon: 'nuget', link: 'https://www.nuget.org/packages/twodog/' }
    ],

    footer: {
      message: '<a href="https://github.com/outfox/2dog?tab=MIT-1-ov-file#readme">2dog</a> is released under the MIT License.',
      copyright: 'Copyright © 2025 <a href="https://github.com/outfox/2dog/graphs/contributors">its contributors</a>'
    },
  }
})
