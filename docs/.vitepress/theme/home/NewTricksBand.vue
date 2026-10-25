<!-- New capabilities as cards, each led by a schematic of what it does. The grid fills row by row, so adding a
     trick is one entry here and one diagram in TrickDiagram.vue. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { iconUrl } from './icons'
import TrickDiagram, { type Diagram } from './TrickDiagram.vue'

const tricks: { title: string; description: string; href: string; diagram: Diagram }[] = [
  {
    title: 'WebGPU driver',
    description: "Mobile Renderer's feature set on <code>browser-wasm</code>, fallback to Compatibility Renderer.",
    href: '/build-configurations#experimental-webgpu',
    diagram: 'webgpu',
  },
  {
    title: 'Multiple instances',
    description: 'Run several isolated engines side by side in one process. Faster tests, faster bakes.',
    href: '/known-issues/single-instance#concurrent-engines',
    diagram: 'instances',
  },
  {
    title: 'Double precision',
    description: 'Build bigger worlds with pre-built double-precision Godot binaries.',
    href: '/build-configurations#double-precision',
    diagram: 'double',
  },
  {
    title: 'Multi-threaded WASM',
    description: '.NET and Godot workers in parallel in the browser, with improved audio mixing.',
    href: '/hosts/web#multithreading',
    diagram: 'wasm-threads',
  },
  {
    title: 'Zero-copy rendering',
    description: 'Render directly into supported UI frameworks, with no CPU copy overhead.',
    href: '/hosts/qt#rendering-and-lifecycle',
    diagram: 'zero-copy',
  },
  {
    title: 'Single-file publish',
    description: 'One executable with the engine, your game and its GDExtensions inside.',
    href: '/configuration#single-file',
    diagram: 'single-file',
  },
  {
    title: 'CPU frame readback',
    description: "Pixels copied into UI as fallback, an improved stand-in for Godot's <code>--wid</code> option.",
    href: '/configuration/qt#session-properties',
    diagram: 'readback',
  },
]
</script>

<template>
  <section class="home-section tricks" id="new-tricks" aria-labelledby="new-tricks-title">
    <div class="home-wrap">
      <p class="home-kicker">
        <span class="gd-icon" :style="{ '--gd-icon': iconUrl('magic_wand') }" aria-hidden="true"></span>Experimental
        &amp; power-user features
      </p>
      <h2 id="new-tricks-title" class="home-h2">New Tricks<a class="home-anchor" href="#new-tricks" aria-label="Permalink to this section">#</a></h2>
      <p class="home-intro">
        <strong>2dog</strong> offers a variety of optional features on top of Godot. They add popular capabilities and optimizations, and stay compatible with core Godot.
      </p>

      <ul class="tricks-grid">
        <li v-for="t in tricks" :key="t.title">
          <a class="trick-card" :href="withBase(t.href)">
            <span class="trick-fig"><TrickDiagram :name="t.diagram" /></span>
            <span class="trick-body">
              <span class="trick-name">{{ t.title }}</span>
              <span class="trick-text" v-html="t.description"></span>
            </span>
          </a>
        </li>
        <li>
          <div class="trick-card soon">
            <span class="trick-fig">
              <span class="brand-chip soon" aria-hidden="true">
                <span class="chip-glyph" :style="{ '--gd-icon': iconUrl('ellipsis_horizontal'), '--glyph': '16px' }"></span>
              </span>
            </span>
            <span class="trick-body">
              <span class="trick-name">Coming soon</span>
              <span class="trick-text"><i>Teaching an old robot new tricks.</i></span>
            </span>
          </div>
        </li>
      </ul>
    </div>
  </section>
</template>

<style>
.tricks {
  background:
    linear-gradient(var(--ed-grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--ed-grid) 1px, transparent 1px),
    var(--ed-dark-2);
  background-size: 32px 32px, 32px 32px, auto;
}

.tricks-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(256px, 1fr));
  gap: 16px;
  margin: 48px 0 0;
  padding: 0;
  list-style: none;
}

.trick-card {
  --trick-panel: var(--ed-dark-1);
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  border: 1px solid var(--ed-seam);
  border-radius: 10px;
  background: var(--ed-base);
  color: var(--ed-text-1);
  text-decoration: none;
  transition: border-color 0.12s ease-out, transform 0.12s ease-out;
}

a.trick-card:hover {
  border-color: color-mix(in srgb, var(--ed-accent) 70%, var(--ed-seam));
  transform: translateY(-2px);
}

/* The diagram sits on a fine 8px editor grid, the scale it is drawn on. */
.trick-fig {
  display: grid;
  place-items: center;
  min-height: 152px;
  padding: 16px 8px;
  border-bottom: 1px solid var(--ed-seam);
  background:
    linear-gradient(var(--ed-grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--ed-grid) 1px, transparent 1px),
    var(--trick-panel);
  background-size: 8px 8px, 8px 8px, auto;
  background-position: center;
}

.trick-fig .trick-svg {
  max-width: 100%;
  height: auto;
}

/* Hovering a card sets its 2dog paths and scene outlines moving. */
a.trick-card:hover .flow,
a.trick-card:focus-visible .flow {
  stroke-dasharray: 4 4;
  animation: trick-flow 0.6s linear infinite;
}

@keyframes trick-flow {
  to {
    stroke-dashoffset: -8;
  }
}

/* Each instance's scene turns a moment after the previous one, then rests; the circle flips edge-on like a coin. */
a.trick-card:hover .spin,
a.trick-card:focus-visible .spin {
  animation: trick-spin 2s cubic-bezier(0.65, 0, 0.35, 1) var(--delay, 0s) infinite;
}

a.trick-card:hover .flip,
a.trick-card:focus-visible .flip {
  animation: trick-flip 2s var(--delay, 0s) infinite;
}

a.trick-card:hover .grow,
a.trick-card:focus-visible .grow {
  animation: trick-grow 3s ease-in-out infinite;
}

@keyframes trick-grow {
  50% {
    transform: scale(1.05);
  }
}

@keyframes trick-spin {
  50%,
  100% {
    transform: rotate(var(--turn));
  }
}

@keyframes trick-flip {
  0% {
    animation-timing-function: cubic-bezier(0.55, 0, 1, 0.45);
  }
  25% {
    transform: scaleX(0.08);
    animation-timing-function: cubic-bezier(0, 0.55, 0.45, 1);
  }
  50%,
  100% {
    transform: scaleX(1);
  }
}

.trick-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 16px 18px 18px;
}

.trick-name {
  font-size: 17px;
  font-weight: 700;
}

.trick-text {
  font-size: 15px;
  line-height: 1.5;
  color: var(--ed-text-2);
}

/* Placeholder: dashed slate like the hosts' Coming Soon card, not a link. */
.trick-card.soon {
  border: 1px dashed color-mix(in srgb, var(--hue-slate) 55%, var(--ed-seam));
  background: transparent;
}

.trick-card.soon .trick-fig {
  border-bottom-style: dashed;
  background: transparent;
}

.trick-card.soon .trick-text {
  color: var(--ed-text-3);
}

@media (max-width: 719px) {
  .tricks-grid {
    margin-top: 32px;
    gap: 12px;
  }
}

@media (prefers-reduced-motion: reduce) {
  a.trick-card .flow,
  a.trick-card .spin,
  a.trick-card .flip,
  a.trick-card .grow {
    animation: none !important;
  }
}
</style>
