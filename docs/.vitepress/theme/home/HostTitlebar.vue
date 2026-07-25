<!-- The .NET process owns this window; Godot runs inside it. -->
<script setup lang="ts">
import { iconUrl } from './icons'
</script>

<template>
  <div class="host-titlebar">
    <span class="host-proc"><span
      class="host-app-icon"
      :style="{ '--gd-icon': iconUrl('gobot') }"
      aria-hidden="true"
    ></span>MyGame.tscn — Godot Engine</span>
    <span class="host-controls" aria-hidden="true"><i>–</i><i>□</i><i>×</i></span>
  </div>
</template>

<style>
/* Host title bar: the inversion, stated as chrome. */
.host-titlebar {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 8px 14px;
  background: var(--ed-dark-2);
  border-bottom: 1px solid var(--ed-seam);
  font-family: var(--vp-font-family-mono);
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
