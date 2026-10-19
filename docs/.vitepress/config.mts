import { defineConfig } from 'vitepress'
import {
  twodogVersionPlugin,
  godotVersionPlugin,
  nativesVersionPlugin,
  twodogVersion,
  godotVersion
} from './plugins/version-markers.mts'
import { columnsPlugin } from './plugins/columns.mts'
import { godotIconsPlugin, godotIconHtml } from './plugins/godot-icons.mts'
import { siteUrl, socialMetadata } from './social-metadata.mjs'

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

  // Emits sitemap.xml into the build output for search engines (Google Search Console).
  // lastUpdated derives per-page <lastmod> from git history; needs a full clone at build time.
  sitemap: {
    hostname: siteUrl
  },
  lastUpdated: true,

  transformHead: socialMetadata,

  title: "2dog",
  titleTemplate: ":title | 2dog",
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
    ['link', { rel: "me", href: "https://mastodon.gamedev.place/@jupiter" }],
    ['link', { rel: "me", href: "https://tiggi.es/@thygrrr" }],
    ['meta', { name: "author", content: "outfox" }],
    ['meta', { name: "theme-color", content: "#10141b" }],
  ],


  themeConfig: {
    siteTitle: false,
    aside: false,
    // Named for screen readers; the light navbar gets a dark-bodied variant.
    logo: { light: '/icon-light.svg', dark: '/icon.svg', alt: '2dog' },

    search: {
      provider: 'local'
    },
    // Real version for the home console; markdown pages use :2dog-version: markers instead.
    twodogVersion,
    godotVersion,
    
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Get Started', link: '/getting-started' },
      { text: 'Web', link: '/hosts/web' },
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
        text: 'Hosts',
        items: [
          { text: gd('container') + "What's a Host?", link: '/hosts/' },
          { text: gd('margincontainer') + '2dog (generic .NET)', link: '/hosts/generic' },
          {
            text: 'Testing',
            items: [
              { text: gd('test_tube') + 'GdUnit', link: '/hosts/gdunit' },
              { text: gd('test_tube') + 'NUnit', link: '/hosts/nunit' },
              { text: gd('test_tube') + 'xUnit', link: '/hosts/xunit' }
            ]
          },
          {
            text: 'Desktop',
            items: [
              { text: gd('window') + 'Avalonia', link: '/hosts/avalonia' },
              { text: gd('window') + 'WinForms', link: '/hosts/winforms' },
              { text: gd('window') + 'WinUI 3', link: '/hosts/winui' }
            ]
          },
          {
            text: 'Experimental',
            items: [
              { text: gd('codeedit') + 'REPL', link: '/hosts/repl' }
            ]
          },
          {
            text: 'Mobile',
            items: [
              { text: gd('mobile_phone') + 'Android', link: '/hosts/android' },
              { text: gd('mobile_phone') + 'MAUI', link: '/hosts/maui' }
            ]
          },
          {
            text: 'Web',
            items: [
              { text: gd('globe') + 'Web (HTML5)', link: '/hosts/web' },
              { text: gd('globe') + 'Blazor', link: '/hosts/blazor' },
              { text: gd('vr_headset') + 'WebXR', link: '/hosts/webxr' }
            ]
          }
        ]
      },
      {
        text: 'Develop',
        items: [
          {
            text: gd('window_terminal') + '2dog (dotnet tool)',
            link: '/dnx-2dog',
            collapsed: true,
            items: [
              {
                text: 'Scaffold',
                collapsed: true,
                items: [
                  { text: gd('bone') + 'add', link: '/cli/add' },
                  { text: gd('sprout') + 'new', link: '/cli/new' },
                  { text: gd('lightbulb') + 'skill', link: '/cli/skill' }
                ]
              },
              {
                text: 'Maintain',
                collapsed: true,
                items: [
                  { text: gd('wrench') + 'doctor', link: '/cli/doctor' },
                  { text: gd('arrows_clockwise') + 'update', link: '/cli/update' }
                ]
              },
              {
                text: 'Inspect',
                collapsed: true,
                items: [
                  { text: gd('help') + 'help', link: '/cli/help' },
                  { text: gd('box') + 'pack', link: '/cli/pack' },
                  { text: gd('codeedit') + 'repl', link: '/cli/repl' }
                ]
              },
              {
                text: 'Version',
                collapsed: true,
                items: [
                  { text: gd('tag') + 'pinning', link: '/cli/pinning' },
                  { text: gd('tag') + 'version', link: '/cli/version' }
                ]
              }
            ]
          },
          {
            text: gd('book_open') + 'API Reference',
            link: '/api-reference',
            collapsed: true,
            items: [
              {
                text: 'Godot',
                collapsed: true,
                items: [
                  { text: gd('arrows_clockwise') + 'GodotInstance', link: '/api/godot-instance' },
                  { text: gd('gobot') + 'GodotSharp', link: '/api/godotsharp' }
                ]
              },
              {
                text: 'twodog',
                collapsed: true,
                items: [
                  { text: gd('script') + 'Engine', link: '/api/engine' }
                ]
              },
              {
                text: 'twodog.fixture',
                collapsed: true,
                items: [
                  { text: gd('script') + 'AssemblyPreloader', link: '/api/assembly-preloader' }
                ]
              },
              {
                text: 'twodog.Testing',
                collapsed: true,
                items: [
                  { text: gd('test_tube') + 'Fixtures', link: '/api/fixture-base' }
                ]
              }
            ]
          },
          {
            text: gd('file_arrow_up') + 'Resources',
            collapsed: true,
            items: [
              { text: 'Resource Import', link: '/import-tool' }
            ]
          },
          {
            text: gd('test_tube') + 'Testing',
            link: '/testing/',
            collapsed: true,
            items: [
              { text: gd('script') + 'Writing Engine Tests', link: '/testing/writing-tests' },
              { text: gd('mouse') + 'Simulating Input', link: '/testing/input' },
              { text: gd('test_tube') + 'GdUnit (GDScript)', link: '/testing/gdunit' },
              { text: gd('test_tube') + 'NUnit', link: '/testing/nunit' },
              { text: gd('test_tube') + 'xUnit', link: '/testing/xunit' }
            ]
          }
        ]
      },
      {
        text: 'Configure',
        items: [
          { text: gd('layers') + 'Build Variants', link: '/build-configurations' },
          { text: gd('wrench') + 'Environment Variables', link: '/configuration/environment-variables' },
          {
            text: gd('wrench') + 'MSBuild Config',
            link: '/configuration',
            collapsed: true,
            items: [
              { text: gd('margincontainer') + '2dog (generic)', link: '/configuration/generic' },
              {
                text: 'Testing',
                items: [
                  { text: gd('test_tube') + 'GdUnit', link: '/configuration/gdunit' },
                  { text: gd('test_tube') + 'NUnit', link: '/configuration/nunit' },
                  { text: gd('test_tube') + 'xUnit', link: '/configuration/xunit' }
                ]
              },
              {
                text: 'Desktop',
                items: [
                  { text: gd('window') + 'Avalonia', link: '/configuration/avalonia' },
                  { text: gd('window') + 'WinForms', link: '/configuration/winforms' },
                  { text: gd('window') + 'WinUI 3', link: '/configuration/winui' }
                ]
              },
              {
                text: 'Experimental',
                items: [
                  { text: gd('codeedit') + 'REPL', link: '/configuration/repl' }
                ]
              },
              {
                text: 'Web',
                items: [
                  { text: gd('globe') + 'Web (HTML5)', link: '/configuration/web' },
                  { text: gd('globe') + 'Blazor', link: '/configuration/blazor' },
                  { text: gd('vr_headset') + 'WebXR', link: '/configuration/webxr' }
                ]
              },
              {
                text: 'Mobile',
                items: [
                  { text: gd('mobile_phone') + 'Android', link: '/configuration/android' },
                  { text: gd('mobile_phone') + 'MAUI', link: '/configuration/maui' }
                ]
              }
            ]
          }
        ]
      },
      {
        text: 'Other',
        items: [
          { text: gd('speech_bubble_question') + 'FAQ', link: '/faq' },
          { text: gd('bug') + 'Troubleshooting', link: '/troubleshooting' },
          {
            text: gd('bug') + 'Known Issues',
            link: '/known-issues/',
            collapsed: true,
            items: [
              { text: gd('window') + 'Single Godot Instance', link: '/known-issues/single-instance' },
              { text: gd('magnifying_glass') + 'xUnit Test Discovery', link: '/known-issues/xunit-discovery' },
              { text: gd('window_terminal') + 'GD.Print in Tests', link: '/known-issues/gd-print-output' },
              { text: gd('script') + 'Spaced Project Names', link: '/known-issues/spaced-project-names' }
            ]
          },
        ]
      }
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/outfox/2dog' },
      { icon: 'discord', link: 'https://discord.gg/GAXdbZCNGT' },
      { icon: 'nuget', link: 'https://www.nuget.org/packages/2dog/' }
    ],

    footer: {
      message: '<b>2dog</b> is Free and Libre Open Source under the <a href="https://github.com/outfox/2dog?tab=MIT-1-ov-file#readme">MIT License</a>.<br/><b>2dog</b> is not affiliated with or endorsed by the Godot Foundation.<br/>Godot is licensed under the <a href="https://godotengine.org/license/">MIT License</a>. <b>@icons</b> made by <a href="https://www.voxy.space">Voxy</a> under the <a href="/icons/LICENSE.txt">MIT License</a>.<br/>Continuous Integration compute graciously supported by <a href="https://runs-on.com">RunsOn</a>.',
      copyright: '<b>2dog</b> is Copyright © 2025-2026 <a href="https://github.com/outfox/2dog/graphs/contributors">its contributors</a>. <b>Lucky &amp; Dogbot</b> illustrations by <a href="https://www.diegorago.com/">Diego Rago</a>.'
    },
  }
})
