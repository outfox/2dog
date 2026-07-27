// Build-time inventory of the icon folders for the editor-home components.
// Ships the actual file stems (casing preserved) per folder so icons.ts can
// resolve lowercase names on the client exactly like the markdown shortcode.
import { defineLoader } from 'vitepress'
import { iconFolders, scanIconFolder } from '../../plugins/godot-icons'

export type IconManifest = Record<string, string[]>

declare const data: IconManifest
export { data }

export default defineLoader({
  watch: ['../../../content/public/icons/*/*.svg'],
  load(): IconManifest {
    return Object.fromEntries(
      [...iconFolders, 'brand'].map((folder) => [folder, scanIconFolder(folder)])
    )
  },
})
