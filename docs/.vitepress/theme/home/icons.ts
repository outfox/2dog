/**
 * Icon mask URLs for the editor-home components. Three sets, one contract:
 * every glyph is applied as a CSS mask and tinted from theme tokens, so both
 * appearances recolor correctly (the SVGs carry baked dark-theme colors).
 *  - icons/node: neutral pictograms (brand content, tree/inspector glyphs)
 *  - icons/godot-editor: the editor's own icons (tools, docks, members)
 *  - icons/godot-theme: GUI furniture (arrows, search, folders, tab menus)
 */
import { withBase } from 'vitepress'

export const iconUrl = (name: string) => `url(${withBase(`/icons/node/${name}.svg`)})`
export const edIconUrl = (name: string) => `url(${withBase(`/icons/godot-editor/${name}.svg`)})`
export const thIconUrl = (name: string) => `url(${withBase(`/icons/godot-theme/${name}.svg`)})`
