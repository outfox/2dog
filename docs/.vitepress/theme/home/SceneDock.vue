<!-- Scene dock: your project and its 2dog hosts, as real links.
     Scene is this page; Import is a real tab — it leads to the import docs. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { iconUrl, edIconUrl, thIconUrl } from './icons'

const hosts = [
  { name: 'MyGame.2dog', role: 'Desktop host', color: 'var(--ed-node-2d)', icon: 'window', link: '/project-layout' },
  { name: 'MyGame.web', role: 'Browser host', color: 'var(--ed-node-gui)', icon: 'globe', link: '/web' },
  { name: 'MyGame.tests', role: 'xUnit host', color: 'var(--ed-node-gold)', icon: 'test_tube', link: '/testing' },
]
</script>

<template>
  <nav class="ed-tree" aria-label="Scene: your project and its 2dog hosts">
    <div class="ed-dock-tabs">
      <span class="ed-dock-tab active" aria-hidden="true">Scene</span>
      <a class="ed-dock-tab" :href="withBase('/import-tool')">Import</a>
      <span class="strip-glyph" :style="{ '--gd-icon': thIconUrl('tabs_menu') }" aria-hidden="true"></span>
    </div>
    <div class="ed-dock-bar" aria-hidden="true">
      <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('Add') }"></span>
      <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('ToolConnect') }"></span>
      <span class="bar-well"><span
        class="bar-well-glyph"
        :style="{ '--gd-icon': thIconUrl('search') }"
      ></span>Filter Nodes</span>
    </div>
    <a class="ed-tree-root" :href="withBase('/project-layout')">
      <span class="tree-arrow" :style="{ '--gd-icon': edIconUrl('GuiTreeArrowDown') }" aria-hidden="true"></span>
      <span class="node-glyph root" :style="{ '--gd-icon': iconUrl('joystick') }" aria-hidden="true"></span>MyGame
      <span class="ed-tree-note">project.godot</span>
    </a>
    <a
      v-for="h in hosts"
      :key="h.name"
      class="ed-tree-item"
      :href="withBase(h.link)"
    >
      <span
        class="node-glyph"
        :style="{ backgroundColor: h.color, '--gd-icon': iconUrl(h.icon) }"
        aria-hidden="true"
      ></span>
      <span class="ed-tree-name">{{ h.name }}</span>
      <span class="ed-tree-note">{{ h.role }}</span>
    </a>
  </nav>
</template>

<style>
/* The tree takes all spare column height; FileSystem below keeps natural size. */
.ed-tree {
  flex: 1;
  padding-bottom: 10px;
}

.ed-tree-root,
.ed-tree-item {
  display: flex;
  align-items: baseline;
  gap: 7px;
  margin: 0 4px;
  padding: 5px 6px;
  border-radius: 3px;
  font-size: 13px;
  color: var(--ed-text-1);
  text-decoration: none;
}

.ed-tree-root {
  margin-top: 4px;
}

.ed-tree-item {
  margin-left: 22px;
  border-left: 1px solid var(--ed-seam);
  border-radius: 0 3px 3px 0;
}

.ed-tree-root:hover,
.ed-tree-item:hover {
  background: var(--ed-raise);
  color: var(--ed-accent);
}

.ed-tree-root:focus-visible,
.ed-tree-item:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

.ed-tree-name {
  font-family: var(--vp-font-family-mono);
  font-size: 12.5px;
}

.ed-tree-note {
  margin-left: auto;
  font-size: 11px;
  color: var(--ed-text-2);
}

/* Narrow: the tree flattens into a chip row under its tab strip. */
@media (max-width: 819px) {
  .ed-tree {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 2px;
    padding: 0 4px 6px;
  }

  .ed-tree .ed-dock-tabs {
    flex-basis: 100%;
    margin: 0 -4px 4px;
  }

  .ed-tree .ed-dock-bar {
    display: none;
  }

  .ed-tree-root {
    margin-top: 0;
  }

  .ed-tree-item {
    margin-left: 0;
    border-left: none;
    border-radius: 3px;
  }

  .ed-tree .tree-arrow,
  .ed-tree-note {
    display: none;
  }
}
</style>
