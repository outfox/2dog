<!-- Center workspace: the 2D toolbar (pure chrome, select tool forever active)
     above the viewport grid where the current scene — the dog — is running. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { edIconUrl, thIconUrl } from './icons'

const viewTools = ['ToolSelect', 'ToolMove', 'ToolRotate', 'ToolScale', 'ToolPan']
const viewLocks = ['Lock', 'Group', 'SnapGrid']
</script>

<template>
  <div class="ed-center">
    <div class="ed-view-bar" aria-hidden="true">
      <span
        v-for="(t, i) in viewTools"
        :key="t"
        class="bar-glyph"
        :class="{ active: i === 0 }"
        :style="{ '--gd-icon': edIconUrl(t) }"
      ></span>
      <span class="bar-sep"></span>
      <span
        v-for="t in viewLocks"
        :key="t"
        class="bar-glyph"
        :style="{ '--gd-icon': edIconUrl(t) }"
      ></span>
      <span class="ed-view-zoom">
        <span class="bar-glyph sm" :style="{ '--gd-icon': thIconUrl('zoom_less') }"></span>
        <span class="ed-zoom-pct">100 %</span>
        <span class="bar-glyph sm" :style="{ '--gd-icon': thIconUrl('zoom_more') }"></span>
      </span>
    </div>

    <div class="ed-viewport">
      <img
        class="ed-mascot"
        :src="withBase('/logo-animated.svg')"
        alt="2dog logotype: a happy white dog over the word 2dog"
        width="450"
        height="231"
      />
      <h1 class="ed-headline">Start, control, embed</h1>
      <h1 class="ed-headline">Godot in&nbsp;.NET</h1>
      <p class="ed-tagline">
        Keep your scenes, scripts, and editor workflow.<br />
        Gain the .NET ecosystem as a treat.
      </p>
      <div class="ed-actions">
        <a class="ed-btn brand" :href="withBase('/getting-started')"><b>Read the Dogs</b></a>
        <a class="ed-btn" href="https://github.com/outfox/2dog">Fetch on GitHub</a>
      </div>
    </div>
  </div>
</template>

<style>
.ed-center {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.ed-view-bar {
  display: flex;
  align-items: center;
  gap: 13px;
  padding: 6px 12px;
  background: var(--ed-base);
  border-bottom: 1px solid var(--ed-seam);
}

.ed-view-zoom {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-left: auto;
}

.ed-zoom-pct {
  font-family: var(--vp-font-family-mono);
  font-size: 11px;
  color: var(--ed-text-2);
}

/* Viewport: the 2D editor's grid, with the current scene running. */
.ed-viewport {
  flex: 1;
  position: relative;
  padding: 36px 40px 44px;
  background:
    linear-gradient(var(--ed-grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--ed-grid) 1px, transparent 1px),
    var(--ed-dark-2);
  background-size: 32px 32px, 32px 32px, auto;
  text-align: center;
}

/* The SVG animates itself (with its own reduced-motion guard); no outer float to compound it. */
.ed-mascot {
  width: min(450px, 80%);
  height: auto;
  margin: 0 auto 8px;
}

.ed-headline {
  margin: 0;
  font-size: clamp(28px, 4vw, 40px);
  line-height: 1.15;
  font-weight: 700;
  letter-spacing: -0.02em;
  color: var(--ed-text-1);
}

.ed-tagline {
  max-width: 44ch;
  margin: 12px auto 0;
  font-size: 15.5px;
  line-height: 1.6;
  color: var(--ed-text-2);
}

.ed-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 10px;
  margin-top: 24px;
}

.ed-btn {
  display: inline-block;
  padding: 8px 18px;
  border: 1px solid var(--ed-seam);
  border-radius: 3px;
  background: var(--ed-raise);
  color: var(--ed-text-1);
  font-size: 14px;
  font-weight: 500;
  text-decoration: none;
  transition: background-color 0.1s ease-out, border-color 0.1s ease-out;
}

.ed-btn:hover {
  border-color: var(--ed-accent);
}

.ed-btn:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: 2px;
}

.ed-btn.brand {
  background: var(--ed-accent);
  border-color: transparent;
  color: var(--ed-on-accent);
}

.ed-btn.brand:hover {
  background: var(--ed-accent-strong);
}

@media (max-width: 819px) {
  .ed-view-bar {
    display: none;
  }

  .ed-viewport {
    padding: 28px 20px 32px;
  }
}
</style>
