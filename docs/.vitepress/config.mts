import { defineConfig } from 'vitepress'
import {
  twodogVersionPlugin,
  godotVersionPlugin,
  nativesVersionPlugin
} from './plugins/version-markers'
import { columnsPlugin } from './plugins/columns'

export default defineConfig({
  srcDir: "content",

  markdown: {
    config(md) {
      // Replace :2dog-version:, :godot-version: and :natives-version:
      // markers with the versions from the repo-root Directory.Build.props.
      md.use(twodogVersionPlugin)
      md.use(godotVersionPlugin)
      md.use(nativesVersionPlugin)
      // ::: columns / :::: column — side-by-side cards.
      md.use(columnsPlugin)
    }
  },

  title: "2dog",
  titleTemplate: ":title – C# Godot Games on the Web",
  description: "Bring an existing C# Godot game to the web without rewriting it",

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
    ['meta', { name: "twitter:title", content: "2dog – Your C# Godot game. Now on the web." }],
    ['meta', { name: "twitter:image", content: "https://2dog.dev/og-image.png" }],
    ['meta', { name: "twitter:image:alt", content: "2dog logo - a happy white dog" }],
    ['meta', { name: "twitter:description", content: "Bring an existing C# Godot game to the web. Keep your scenes, scripts, and editor workflow." }],
    ['meta', { property: "og:type", content: "website" }],
    ['meta', { property: "og:url", content: "https://2dog.dev" }],
    ['meta', { property: "og:title", content: "2dog – Your C# Godot game. Now on the web." }],
    ['meta', { property: "og:description", content: "Bring an existing C# Godot game to the web. Keep your scenes, scripts, and editor workflow." }],
    ['meta', { property: "og:image", content: "https://2dog.dev/og-image.png" }],
  ],


  themeConfig: {
    siteTitle: false,
    logo: '/icon.svg',
    
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Get Started', link: '/getting-started' },
      { text: 'Web', link: '/web' },
      { text: 'API', link: '/api-reference' }
    ],

    sidebar: [
      {
        text: 'Start Here',
        items: [
          { text: 'Getting Started', link: '/getting-started' },
          { text: 'Recommended Project Layout', link: '/project-layout' },
          { text: 'Core Concepts', link: '/concepts' }
        ]
      },
      {
        text: 'Build and Ship',
        items: [
          { text: 'Converting a Godot Project', link: '/convert' },
          { text: 'Creating a New Project', link: '/templates' },
          { text: 'Web / Browser (WASM)', link: '/web' }
        ]
      },
      {
        text: 'Develop',
        items: [
          { text: 'Testing with xUnit', link: '/testing' },
          { text: 'Resource Import', link: '/import-tool' }
        ]
      },
      {
        text: 'Configure',
        items: [
          { text: 'Choosing a Native Variant', link: '/build-configurations' },
          { text: 'MSBuild Configuration', link: '/configuration' }
        ]
      },
      {
        text: 'Reference',
        items: [
          { text: 'API Reference', link: '/api-reference' },
          { text: 'FAQ', link: '/faq' }
        ]
      },
      {
        text: 'Troubleshooting',
        items: [
          {
            text: 'Known Issues',
            link: '/known-issues/',
            collapsed: true,
            items: [
              { text: 'Single Godot Instance', link: '/known-issues/single-instance' },
              { text: 'xUnit Test Discovery', link: '/known-issues/xunit-discovery' },
              { text: 'GD.Print in Tests', link: '/known-issues/gd-print-output' }
            ]
          }
        ]
      }
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/outfox/2dog' },
      { icon: 'discord', link: 'https://discord.gg/GAXdbZCNGT' },
      { icon: 'nuget', link: 'https://www.nuget.org/packages/2dog/' }
    ],

    footer: {
      message: '<a href="https://github.com/outfox/2dog?tab=MIT-1-ov-file#readme">2dog</a> is released under the MIT License.',
      copyright: 'Copyright © 2025 <a href="https://github.com/outfox/2dog/graphs/contributors">its contributors</a>'
    },
  }
})
