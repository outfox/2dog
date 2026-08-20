<!-- FileSystem dock: the game exactly as the editor sees it — untouched. The hosts
     are missing here because .gdignore hides them, which IS the claim.
     Chrome matched to the shipping dock: path bar, Favorites section, the selected
     res:// row, folder icons modulated with folder_icon_color, and the open scene's
     name painted with the accent (filesystem_dock.cpp:339). -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { edIconUrl, thIconUrl } from './icons'
import { plain } from './content'

/* name accepts inline HTML — see content.ts for the rule. */
const entries = [
  { name: 'scenes', folder: true },
  { name: 'scripts', folder: true },
  { name: 'sprites', folder: true },
  { name: 'icon.svg', icon: 'Image' },
  { name: 'main.tscn', icon: 'PackedScene', open: true },
  { name: 'project.godot', icon: 'GodotMonochrome' },
]
</script>

<template>
  <section class="ed-files" data-nosnippet aria-label="FileSystem: your game files, untouched by 2dog">
    <div class="ed-dock-tabs" aria-hidden="true">
      <span class="ed-dock-tab active">FileSystem</span>
      <span class="strip-glyph" :style="{ '--gd-icon': thIconUrl('tabs_menu') }"></span>
    </div>

    <!-- Path bar and split toggle are chrome: dimmed and inert. -->
    <div class="ed-file-bar" aria-hidden="true">
      <span class="bar-glyph sm" :style="{ '--gd-icon': edIconUrl('Back') }"></span>
      <span class="bar-glyph sm" :style="{ '--gd-icon': edIconUrl('Forward') }"></span>
      <span class="ed-file-path">res://</span>
      <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('Panels2') }"></span>
    </div>

    <div class="ed-file-tree">
      <div class="ed-file-row is-chrome">
        <span class="file-glyph star" :style="{ '--gd-icon': edIconUrl('Favorites') }" aria-hidden="true"></span>Favorites
      </div>
      <div class="ed-file-row root is-selected">
        <span class="tree-arrow" :style="{ '--gd-icon': edIconUrl('GuiTreeArrowDown') }" aria-hidden="true"></span>
        <span class="file-glyph folder" :style="{ '--gd-icon': edIconUrl('Folder') }" aria-hidden="true"></span>res://
      </div>
      <!-- Every entry answers the same question — what conversion did to this tree —
           so they all lead to Project Layout; the label names the destination. -->
      <a
        v-for="e in entries"
        :key="e.name"
        class="ed-file-row child"
        :class="{ 'is-open': e.open }"
        :href="withBase('/project-layout')"
        :aria-label="`${plain(e.name)} — Project Layout`"
      >
        <span
          v-if="e.folder"
          class="tree-arrow"
          :style="{ '--gd-icon': edIconUrl('GuiTreeArrowRight') }"
          aria-hidden="true"
        ></span>
        <span v-else class="tree-arrow-space" aria-hidden="true"></span>
        <span
          class="file-glyph"
          :class="{ folder: e.folder }"
          :style="{ '--gd-icon': edIconUrl(e.folder ? 'Folder' : e.icon!) }"
          aria-hidden="true"
        ></span><span v-html="e.name"></span>
      </a>
    </div>

    <p class="ed-files-note">
      Your files, exactly as they were. The hosts sit beside them as
      <code>.gdignore</code>'d siblings.
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

/* The path row shares the dock-bar rhythm without its bottom seam. */
.ed-file-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 8px 0;
}

.ed-file-path {
  flex: 1;
  min-width: 0;
  padding: 3px 8px;
  border: 1px solid var(--ed-seam);
  border-radius: 3px;
  background: var(--ed-dark-2);
  font-size: 11.5px;
  color: var(--ed-text-3);
  white-space: nowrap;
  overflow: hidden;
}

.ed-file-tree {
  padding-top: 6px;
}

.ed-file-row {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0 4px;
  padding: 3px 6px;
  border-radius: 3px;
  font-size: 12.5px;
  color: var(--ed-text-1);
}

/* Godot paints the selected row as a full-width bar; unfocused, it reads grey. */
.ed-file-row.is-selected {
  background: var(--ed-raise);
}

/* The open scene's name carries the accent — filesystem_dock.cpp:339. */
.ed-file-row.is-open {
  color: var(--ed-accent);
}

/* Sections the editor shows but this dock does not drive. */
.ed-file-row.is-chrome {
  color: var(--ed-text-3);
}

.ed-file-row.child {
  margin-left: 22px;
  border-left: 1px solid var(--ed-seam);
  border-radius: 0 3px 3px 0;
}

/* Entry rows are links; hover matches the scene tree's, Godot-tree style. */
a.ed-file-row {
  text-decoration: none;
}

a.ed-file-row:hover {
  background: var(--ed-raise);
  color: var(--ed-accent);
}

a.ed-file-row:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

/* The open scene keeps its accent name; only the row ground reacts. */
a.ed-file-row.is-open:hover {
  color: var(--ed-accent-strong);
}

/* Files sit under the folders' chevron column, not beside it. */
.tree-arrow-space {
  flex: none;
  width: 12px;
}

.file-glyph {
  flex: none;
  width: 15px;
  height: 15px;
  background-color: var(--ed-text-2);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

/* set_icon_modulate(folder_icon_color) — the only tinted glyph in the dock. */
.file-glyph.folder {
  background-color: var(--ed-folder);
}

.file-glyph.star {
  background-color: var(--ed-text-3);
  opacity: 0.7;
}

.ed-files-note {
  margin: 10px 8px 10px;
  font-size: 11px;
  line-height: 1.55;
  color: var(--ed-text-2);
}

.ed-files-note code {
  font-family: var(--vp-font-family-mono);
  font-size: 10.5px;
}

</style>
