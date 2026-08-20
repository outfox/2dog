<!-- The window frame the whole page lives in. The title is the editor's own, verbatim;
     what proves .NET is holding the leash sits in the docks and the console below. -->
<script setup lang="ts">
import { iconUrl } from './icons'
</script>

<template>
  <!-- data-nosnippet across the chrome: Google must never quote the editor prop
       text (menus, docks, fake console) as the search snippet — the viewport hero
       and the meta description are the page's words. -->
  <div class="host-titlebar" data-nosnippet>
    <span class="host-proc"><span
      class="host-app-icon"
      :style="{ '--gd-icon': iconUrl('gobot') }"
      aria-hidden="true"
    ></span>main.tscn - 2dog - Godot Engine</span>
    <span class="host-controls" aria-hidden="true"><i>–</i><i>□</i><i>×</i></span>
  </div>
</template>

<style>
/* Window chrome: the deepest step, carrying the app icon and window controls. */
.host-titlebar {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 8px 14px;
  background: var(--ed-dark-2);
  border-bottom: 1px solid var(--ed-seam);
  font-size: 12px;
  color: var(--ed-text-1);
}

/* The window's app icon: gobot, wearing Godot blue. */
.host-app-icon {
  display: inline-block;
  width: 14px;
  height: 14px;
  margin-right: 8px;
  vertical-align: -2.5px;
  background-color: var(--ed-accent);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

.host-controls {
  display: flex;
  gap: 10px;
  margin-left: auto;
  color: var(--ed-text-2);
}

.host-controls i {
  font-style: normal;
}
</style>
