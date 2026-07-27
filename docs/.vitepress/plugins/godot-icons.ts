// Godot pictogram icons for the docs.
//
// The MIT-licensed icon set in content/public/icons ships every glyph in six
// pre-tinted Godot category colors. The docs reference the neutral node/,
// godot-editor/, and godot-theme/ files and tint via CSS mask (.gd-icon in
// theme/custom.css), so icons follow the light and dark token palettes
// instead of baked-in dark-theme colors.
//
// Markdown usage (not replaced inside inline code or fenced blocks):
//
//   :gd-bone:        -> neutral glyph, inherits the surrounding text color
//   :gd-bone@gold:   -> tinted with a categorical or semantic token color
//
// Unknown icon names or tints fail the build.

import { readdirSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import type MarkdownIt from 'markdown-it'
import type { StateCore } from 'markdown-it/index.js'
import type Token from 'markdown-it/lib/token.mjs'

const iconsRoot = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../content/public/icons'
)

// Keys are the lowercased file stem regardless of on-disk casing. On name
// collisions the earlier folder wins: node over godot-editor over godot-theme.
export const iconFolders = ['node', 'godot-editor', 'godot-theme']

// Actual file stems (casing preserved) of every .svg in an icons subfolder.
export function scanIconFolder(folder: string): string[] {
  return readdirSync(resolve(iconsRoot, folder))
    .filter((f) => f.endsWith('.svg'))
    .map((f) => f.slice(0, -4))
}

const inventory = new Map<string, string>()
for (const folder of iconFolders) {
  for (const stem of scanIconFolder(folder)) {
    const key = stem.toLowerCase()
    if (!inventory.has(key)) inventory.set(key, `/icons/${folder}/${stem}.svg`)
  }
}

// Tint -> token mapping lives in theme/custom.css (.gd-icon--*).
const tints = new Set(['2d', '3d', 'gui', 'gold', 'anim', 'ok', 'warn', 'err', 'accent', 'dim'])

const pattern = /:gd-([a-z0-9_]+)(?:@([a-z0-9]+))?:/g

export function godotIconHtml(name: string, tint?: string): string {
  const url = inventory.get(name)
  if (!url) {
    throw new Error(
      `Unknown Godot icon ":gd-${name}:" — no ${name}.svg in content/public/icons/{${iconFolders.join(',')}}`
    )
  }
  if (tint && !tints.has(tint)) {
    throw new Error(`Unknown Godot icon tint "@${tint}" — expected one of: ${[...tints].join(', ')}`)
  }
  const cls = tint ? ` gd-icon--${tint}` : ''
  return `<span class="gd-icon${cls}" style="--gd-icon:url('${url}')" aria-hidden="true"></span>`
}

function splitTextToken(token: Token, state: StateCore): Token[] {
  const out: Token[] = []
  let last = 0
  for (const match of token.content.matchAll(pattern)) {
    if (match.index > last) {
      const text = new state.Token('text', '', 0)
      text.content = token.content.slice(last, match.index)
      out.push(text)
    }
    const icon = new state.Token('gd_icon', '', 0)
    icon.meta = { name: match[1], tint: match[2] }
    out.push(icon)
    last = match.index + match[0].length
  }
  if (last < token.content.length) {
    const text = new state.Token('text', '', 0)
    text.content = token.content.slice(last)
    out.push(text)
  }
  return out
}

export function godotIconsPlugin(md: MarkdownIt) {
  // Runs directly after inline parsing, before anchor/slug rules, so heading
  // anchors never see the shortcode text.
  md.core.ruler.after('inline', 'godot_icons', (state) => {
    for (const token of state.tokens) {
      if (token.type !== 'inline' || !token.children) continue
      if (!token.content.includes(':gd-')) continue
      token.children = token.children.flatMap((child) =>
        child.type === 'text' && child.content.includes(':gd-')
          ? splitTextToken(child, state)
          : child
      )
    }
  })

  md.renderer.rules.gd_icon = (tokens, idx) =>
    godotIconHtml(tokens[idx].meta.name, tokens[idx].meta.tint)
}
