<!--
THESIS: The docs are a Godot editor session, rendered faithfully enough to be recognized in a second —
then the contents give the ownership inversion away: the hosts docked beside your own files, the Script
row, the console transcript. Refuses the category default: gradient wordmark hero over feature cards.
OWN-WORLD: Godot editor dark theme from source — #242424 ground, #1c1c1c/#161616 panels, 1px #0f0f0f seams,
#579eff accent, node colors #8da5f3/#fc7f7f/#8eef97, Inter UI + JetBrains Mono machine text; docks, tree
items, tabs, console lines — never web cards.
STORY: A Godot C# dev recognizes their editor instantly, then notices three host projects docked beside
their untouched game; the console and the real commands prove .NET drives it; they click Read the Dogs.
FIRST VIEWPORT: window title bar on top; Scene + FileSystem docks (real host projects; the game's files,
visibly untouched) left; center viewport with 2D toolbar, the running dog, headline, two actions;
Inspector dock right (real tabs, working filter, Signals connections); output console below. Nothing
renders outside the window.
FORM: Godot editor interface grammar — grounded candidate 4, seed 93cdfc31; dock chrome matched against
the shipping 4.7 editor. One component per editor region lives in home/; the shared dock grammar
(tab strips, toolbars, glyph masks) is home/dock.css; icon-mask helpers are home/icons.ts.
-->
<script setup lang="ts">
import HostTitlebar from './home/HostTitlebar.vue'
import EditorMenubar from './home/EditorMenubar.vue'
import SceneDock from './home/SceneDock.vue'
import FileSystemDock from './home/FileSystemDock.vue'
import ViewportPane from './home/ViewportPane.vue'
import InspectorDock from './home/InspectorDock.vue'
import BottomPanel from './home/BottomPanel.vue'
import './home/dock.css'
</script>

<template>
  <!-- <main>: the home layout renders no other main landmark (VPDoc's is doc-pages only). -->
  <main class="editor-home" aria-label="2dog: start, control, and embed Godot in .NET">
    <div class="host-window">
      <HostTitlebar />
      <EditorMenubar />

      <div class="ed-body">
        <!-- Left dock column: Scene above, FileSystem below, exactly like the shipping editor. -->
        <div class="ed-left">
          <SceneDock />
          <FileSystemDock />
        </div>

        <ViewportPane />
        <InspectorDock />
      </div>

      <BottomPanel />
    </div>
  </main>
</template>

<style>
.editor-home {
  padding: 32px 24px 8px;
  max-width: 1280px;
  margin: 0 auto;
}

.host-window {
  border: 1px solid var(--ed-seam);
  border-radius: 6px;
  background: var(--ed-base);
  overflow: hidden;
  box-shadow: 0 12px 32px -12px rgba(0, 0, 0, 0.45);
}

.ed-body {
  display: grid;
  grid-template-columns: 236px 1fr 272px;
}

.ed-left {
  display: flex;
  flex-direction: column;
  background: var(--ed-dark-1);
  border-right: 1px solid var(--ed-seam);
}

/* Narrow: the inspector redocks below the viewport (its own rules live in
   InspectorDock.vue); here only the grid re-flows. */
@media (max-width: 1139px) {
  .ed-body {
    grid-template-columns: 236px 1fr;
  }
}

@media (max-width: 819px) {
  .editor-home {
    padding: 20px 16px 4px;
  }

  .ed-body {
    grid-template-columns: 1fr;
  }

  .ed-left {
    border-right: none;
    border-bottom: 1px solid var(--ed-seam);
  }
}
</style>
