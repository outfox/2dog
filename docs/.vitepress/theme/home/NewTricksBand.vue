<!-- New capabilities, with linked highlights styled like the Inspector's property rows. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { brIconUrl, webgpuLogoUrl } from './icons'

const tricks = [
  {
    title: 'WebGPU rendering',
    description: 'Mobile Forward Renderer on desktop and single-threaded web builds.',
    href: '/build-configurations#experimental-webgpu',
  },
  {
    title: 'Multiple instances',
    description: 'Run engines in one process side by side with isolated concurrent hosting.',
    href: '/known-issues/single-instance#concurrent-engines',
  },
  {
    title: 'Double precision',
    description: 'Build bigger worlds with pre-built double-precision Godot binaries.',
    href: '/build-configurations#double-precision',
  },
  {
    title: 'Multi-threaded WebAssembly',
    description: '.NET and Godot workers in parallel in the browser, and much better sound.',
    href: '/hosts/web#multithreading',
  },
]
</script>

<template>
  <section class="home-section gpu" aria-labelledby="new-tricks-title">
    <div class="home-wrap gpu-grid">
      <div>
        <p class="home-kicker">Experimental & power-user features</p>
        <div class="gpu-marks" aria-hidden="true">
          <span class="brand-chip">
            <img :src="webgpuLogoUrl()" alt="" width="35" height="22" />
          </span>
          <span class="brand-chip" style="--mark: var(--hue-sky)">
            <span class="chip-glyph" :style="{ '--gd-icon': brIconUrl('webassembly') }"></span>
          </span>
          <span class="brand-chip" style="--mark: var(--hue-violet)">
            <span class="chip-glyph" :style="{ '--gd-icon': brIconUrl('dotnet') }"></span>
          </span>
        </div>
        <h2 id="new-tricks-title" class="home-h2">New Tricks</h2>
        <p class="home-intro">
          <strong>2dog</strong> offers a variety of optional features on top of Godot. They maintain general compatibility with the core Godot engine, adding new & popular capabilities and optimizations.
        </p>
      </div>

      <dl class="gpu-props">
        <div v-for="trick in tricks" :key="trick.title" class="gpu-prop">
          <dt><a :href="withBase(trick.href)">{{ trick.title }}</a></dt>
          <dd>{{ trick.description }}</dd>
        </div>
      </dl>
    </div>
  </section>
</template>

<style>
.gpu {
  background:
    linear-gradient(var(--ed-grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--ed-grid) 1px, transparent 1px),
    var(--ed-dark-2);
  background-size: 32px 32px, 32px 32px, auto;
}

.gpu-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr);
  gap: 64px;
  align-items: center;
}

.gpu-marks {
  display: flex;
  gap: 12px;
  margin-bottom: 28px;
}

.gpu-actions {
  margin-top: 32px;
}

.gpu-props {
  margin: 0;
  border: 1px solid var(--ed-seam);
  border-radius: 10px;
  background: var(--ed-base);
  overflow: hidden;
}

.gpu-prop {
  display: grid;
  grid-template-columns: 160px minmax(0, 1fr);
  gap: 16px;
  padding: 16px 20px;
}

.gpu-prop + .gpu-prop {
  border-top: 1px solid var(--ed-seam);
}

.gpu-prop dt {
  font-size: 15px;
}

.gpu-prop dt a {
  color: var(--ed-accent);
  text-decoration: underline;
  text-underline-offset: 3px;
}

.gpu-prop dd {
  margin: 0;
  font-size: 15px;
  color: var(--ed-text-2);
}

@media (max-width: 959px) {
  .gpu-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 40px;
  }
}

@media (max-width: 479px) {
  .gpu-prop {
    grid-template-columns: minmax(0, 1fr);
    gap: 2px;
  }
}
</style>
