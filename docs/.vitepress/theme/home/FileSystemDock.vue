<!-- FileSystem dock: the game exactly as the editor sees it — untouched. The hosts
     are missing here because .gdignore hides them, which IS the claim. -->
<script setup lang="ts">
import { edIconUrl, thIconUrl } from './icons'

const files = [
  { name: 'scenes', icon: 'Folder', folder: true },
  { name: 'scripts', icon: 'Folder', folder: true },
  { name: 'sprites', icon: 'Folder', folder: true },
  { name: 'icon.svg', icon: 'Image' },
  { name: 'main.tscn', icon: 'PackedScene' },
  { name: 'project.godot', icon: 'GodotMonochrome' },
]
</script>

<template>
  <section class="ed-files" aria-label="FileSystem: your game files, untouched by 2dog">
    <div class="ed-dock-tabs" aria-hidden="true">
      <span class="ed-dock-tab active">FileSystem</span>
      <span class="strip-glyph" :style="{ '--gd-icon': thIconUrl('tabs_menu') }"></span>
    </div>
    <div class="ed-file-row root">
      <span class="tree-arrow" :style="{ '--gd-icon': edIconUrl('GuiTreeArrowDown') }" aria-hidden="true"></span>
      <span class="file-glyph folder" :style="{ '--gd-icon': thIconUrl('folder') }" aria-hidden="true"></span>res://
    </div>
    <div v-for="f in files" :key="f.name" class="ed-file-row">
      <span
        class="file-glyph"
        :class="{ folder: f.folder }"
        :style="{ '--gd-icon': f.folder ? thIconUrl('folder') : edIconUrl(f.icon) }"
        aria-hidden="true"
      ></span>{{ f.name }}
    </div>
    <p class="ed-files-note">
      Your files, exactly as they were. The hosts sit beside them,
      <code>.gdignore</code>'d — the editor never even sees them.
    </p>
  </section>
</template>

<style>
/* Plain rows — this dock's job is to show nothing changed. It hugs the bottom
   of the column at natural height; the Scene tree above soaks up spare space. */
.ed-files {
  display: flex;
  flex-direction: column;
  border-top: 1px solid var(--ed-seam);
}

.ed-file-row {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0 4px;
  padding: 3px 6px;
  font-family: var(--vp-font-family-mono);
  font-size: 12px;
  color: var(--ed-text-1);
}

.ed-file-row.root {
  margin-top: 6px;
}

.ed-file-row:not(.root) {
  margin-left: 22px;
  border-left: 1px solid var(--ed-seam);
}

.file-glyph {
  flex: none;
  width: 15px;
  height: 15px;
  background-color: var(--ed-text-2);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

/* Godot paints folders in the accent-tinted folder color. */
.file-glyph.folder {
  background-color: var(--ed-node-2d);
}

.ed-files-note {
  margin: 8px 8px 10px;
  padding-top: 0;
  font-size: 11px;
  line-height: 1.55;
  color: var(--ed-text-2);
}

.ed-files-note code {
  font-family: var(--vp-font-family-mono);
  font-size: 10.5px;
}

@media (max-width: 819px) {
  .ed-files {
    display: none;
  }
}
</style>
