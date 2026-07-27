/**
 * Icon mask URLs for the editor-home components. Three sets, one contract:
 * every glyph is applied as a CSS mask and tinted from theme tokens, so both
 * appearances recolor correctly (the SVGs carry baked dark-theme colors).
 *  - icons/node: neutral pictograms (brand content, tree/inspector glyphs)
 *  - icons/godot-editor: the editor's own icons (tools, docks, members)
 *  - icons/godot-theme: GUI furniture (arrows, search, folders, tab menus)
 *  - icons/brand: vendored brand marks (simple-icons, CC0)
 *
 * Names are matched case-insensitively against the build-time inventory in
 * icons.data.ts. iconUrl falls through node -> godot-editor -> godot-theme
 * (same precedence as the :gd-...: markdown shortcode); the folder-specific
 * helpers stay scoped for glyphs that exist in several sets. Unknown names
 * throw, which fails the build during SSR rendering.
 */
import { withBase } from 'vitepress'
import { data as manifest } from './icons.data'

const folderMaps = new Map(
  Object.entries(manifest).map(([folder, stems]) => [
    folder,
    new Map(stems.map((s) => [s.toLowerCase(), s])),
  ])
)

function iconMaskUrl(folders: string[], name: string): string {
  const key = name.toLowerCase()
  for (const folder of folders) {
    const stem = folderMaps.get(folder)?.get(key)
    if (stem !== undefined) return `url(${withBase(`/icons/${folder}/${stem}.svg`)})`
  }
  throw new Error(`Unknown icon "${name}" — no ${key}.svg in content/public/icons/{${folders.join(',')}}`)
}

export const iconUrl = (name: string) => iconMaskUrl(['node', 'godot-editor', 'godot-theme'], name)
export const edIconUrl = (name: string) => iconMaskUrl(['godot-editor'], name)
export const thIconUrl = (name: string) => iconMaskUrl(['godot-theme'], name)
export const brIconUrl = (name: string) => iconMaskUrl(['brand'], name)
