import { defineConfig } from 'vitepress'
import {
  twodogVersionPlugin,
  godotVersionPlugin,
  nativesVersionPlugin,
  twodogVersion
} from './plugins/version-markers'
import { columnsPlugin } from './plugins/columns'
import { godotIconsPlugin, godotIconHtml } from './plugins/godot-icons'

// Sidebar node glyphs; the section's --tree-color tints them (theme/custom.css).
const gd = (name: string) => `${godotIconHtml(name)} `

export default defineConfig({
  srcDir: "content",

  // The vendored icon sets ship their own README.md; without this VitePress
  // renders it as a real page (/public/icons/godot-editor/README) and puts it
  // in the search index.
  srcExclude: ['public/**/*.md'],

  // The Editor, Inverted: the audience's editor is dark; light is the editor's light theme.
  appearance: 'dark',

  markdown: {
    config(md) {
      // Replace :2dog-version:, :godot-version: and :natives-version:
      // markers with the versions from the repo-root Directory.Build.props.
      md.use(twodogVersionPlugin)
      md.use(godotVersionPlugin)
      md.use(nativesVersionPlugin)
      // ::: columns / :::: column — side-by-side cards.
      md.use(columnsPlugin)
      // :gd-name: / :gd-name@tint: — Godot pictogram icons as CSS masks.
      md.use(godotIconsPlugin)
    }
  },

  title: "2dog",
  titleTemplate: ":title 🦴 Godot in .NET",
  description: "2dog is Godot C# running in .NET! Export for HTML5, run unit tests, embed and automate. Keep your scenes, scripts, and Godot workflow.",

  head: [
    // Self-hosted (public/fonts, vendored from godot/thirdparty/fonts). Inter paints
    // nearly every pixel of text, so it leads; discovered from CSS it would start a
    // round-trip late and widen the font-display: swap window.
    ['link', { rel: 'preload', href: '/fonts/Inter_Regular.woff2', as: 'font', type: 'font/woff2', crossorigin: '' }],
    ['link', { rel: 'preload', href: '/fonts/JetBrainsMono_Regular.woff2', as: 'font', type: 'font/woff2', crossorigin: '' }],
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
    ['meta', { name: "twitter:description", content: "2dog is Godot C# running in .NET! Export for HTML5, run unit tests, embed and automate. Keep your scenes, scripts, and Godot workflow." }],
    ['meta', { property: "og:type", content: "website" }],
    ['meta', { property: "og:url", content: "https://2dog.dev" }],
    ['meta', { property: "og:title", content: "2dog – Your C# Godot game. Now on the web." }],
    ['meta', { property: "og:description", content: "2dog is Godot C# running in .NET! Export for HTML5, run unit tests, embed and automate. Keep your scenes, scripts, and Godot workflow." }],
    ['meta', { property: "og:image", content: "https://2dog.dev/og-image.png" }],
  ],


  themeConfig: {
    siteTitle: false,
    // Named for screen readers; the light navbar gets a dark-bodied variant.
    logo: { light: '/icon-light.svg', dark: '/icon.svg', alt: '2dog' },

    search: {
      provider: 'local'
    },
    // Real version for the home console; markdown pages use :2dog-version: markers instead.
    twodogVersion,
    
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Get Started', link: '/getting-started' },
      { text: 'Web', link: '/web' },
      { text: 'API', link: '/api-reference' }
    ],

    sidebar: [
      {
        text: 'Start here',
        items: [
          { text: gd('play') + 'Getting Started', link: '/getting-started' },
          { text: gd('lightbulb') + 'Core Concepts', link: '/concepts' },
          { text: gd('folder_open') + 'Project Layout', link: '/project-layout' }
        ]
      },
      {
        text: 'Build and Ship',
        items: [
          { text: gd('bone') + 'Existing Projects', link: '/add' },
          { text: gd('sprout') + 'New Projects', link: '/templates' },
          { text: gd('globe') + 'Web / Browser (WASM)', link: '/web' }
        ]
      },
      {
        text: 'Hosts',
        items: [
          { text: gd('plug') + 'What Is a Host?', link: '/hosts/' },
          { text: gd('desktop') + 'Console', link: '/hosts/console' },
          { text: gd('globe') + 'Browser', link: '/hosts/web' },
          { text: gd('test_tube') + 'xUnit', link: '/hosts/xunit' },
          { text: gd('window') + 'WinForms', link: '/hosts/winforms' }
        ]
      },
      {
        text: 'Develop',
        items: [
          {
            text: gd('book_open') + 'API Reference',
            link: '/api-reference',
            collapsed: true,
            items: [
              {
                text: 'twodog',
                items: [
                  { text: gd('script') + 'Engine', link: '/api/engine' }
                ]
              },
              {
                text: 'Godot',
                items: [
                  { text: gd('arrows_clockwise') + 'GodotInstance', link: '/api/godot-instance' }
                ]
              },
              {
                text: 'twodog.Testing',
                items: [
                  { text: gd('test_tube') + 'FixtureBase', link: '/api/fixture-base' },
                  { text: gd('test_tube') + 'Fixture', link: '/api/fixture' },
                  { text: gd('test_tube') + 'HeadlessFixture', link: '/api/headless-fixture' }
                ]
              },
              {
                text: 'twodog.Testing.Xunit',
                items: [
                  { text: gd('layers') + 'RenderingCollection', link: '/api/rendering-collection' },
                  { text: gd('layers') + 'HeadlessCollection', link: '/api/headless-collection' }
                ]
              }
            ]
          },
          { text: gd('file_arrow_down') + 'Resource Import', link: '/import-tool' },
          { text: gd('test_tube') + 'Testing with xUnit', link: '/testing' }
        ]
      },
      {
        text: 'Configure',
        items: [
          { text: gd('layers') + 'Choosing a Variant', link: '/build-configurations' },
          { text: gd('wrench') + 'MSBuild Configuration', link: '/configuration' }
        ]
      },
      {
        text: 'Other',
        items: [
          { text: gd('speech_bubble_question') + 'FAQ', link: '/faq' },
          { text: gd('archive') + 'Misc', link: '/misc' },
          {
            text: gd('bug') + 'Known Issues',
            link: '/known-issues/',
            collapsed: true,
            items: [
              { text: gd('window') + 'Single Godot Instance', link: '/known-issues/single-instance' },
              { text: gd('magnifying_glass') + 'xUnit Test Discovery', link: '/known-issues/xunit-discovery' },
              { text: gd('window_terminal') + 'GD.Print in Tests', link: '/known-issues/gd-print-output' }
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
      message: '<b>2dog</b> is Free and Libre Open Source under the <a href="https://github.com/outfox/2dog?tab=MIT-1-ov-file#readme">MIT License</a>. <br/><b>@icons</b> made by <a href="https://www.voxy.space">Voxy</a> under the <a href="/icons/LICENSE.txt">MIT License</a>.',
      copyright: '<b>2dog</b> is Copyright © 2025 <a href="https://github.com/outfox/2dog/graphs/contributors">its contributors</a>'
    },
  }
})
