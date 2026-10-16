<!-- WebGPU spotlight. The spec list on the right borrows the Inspector's property rows. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { brIconUrl, webgpuLogoUrl } from './icons'

const props = [
  { key: 'Renderer', value: 'WebGPU with WebGL&nbsp;2 fallback' },
  { key: 'Runtime', value: '.NET 10 browser-wasm + desktop' },
]
</script>

<template><!--
  <section class="home-section gpu" aria-labelledby="gpu-title">
    <div class="home-wrap gpu-grid">
      <div>
        <div class="gpu-marks" aria-hidden="true">
          <span class="brand-chip">
            <img :src="webgpuLogoUrl()" alt="" width="35" height="22" />
          </span>
          <span class="brand-chip" style="--mark: var(--mark-wasm)">
            <span class="chip-glyph" :style="{ '--gd-icon': brIconUrl('webassembly') }"></span>
          </span>
          <span class="brand-chip" style="--mark: var(--mark-dotnet)">
            <span class="chip-glyph" :style="{ '--gd-icon': brIconUrl('dotnet') }"></span>
          </span>
        </div>
        <p class="home-kicker">New in 2dog</p>
        <h2 id="gpu-title" class="home-h2">WebGPU rendering driver</h2>
        <p class="home-intro">
          Design for the Forward+ or Mobile renderers instead of Compatibility.<br/>It's a modern GPU interface for modern browsers: less
          overhead, access to compute shaders, with performance &amp; visuals close to desktop builds. <br/><i>(older browsers can still use WebGL&nbsp;2)</i>
        </p>
        <div class="home-actions gpu-actions">
          <a class="home-btn primary" :href="withBase('/hosts/web')">Ship to the web</a>
          <a class="home-btn" :href="withBase('/hosts/blazor')">Embed with Blazor</a>
        </div>
      </div>

      <dl class="gpu-props">
        <div v-for="p in props" :key="p.key" class="gpu-prop">
          <dt>{{ p.key }}</dt>
          <dd v-html="p.value"></dd>
        </div>
      </dl>
    </div>
  </section>
  -->
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
  grid-template-columns: 112px minmax(0, 1fr);
  gap: 16px;
  padding: 16px 20px;
}

.gpu-prop + .gpu-prop {
  border-top: 1px solid var(--ed-seam);
}

.gpu-prop dt {
  font-size: 15px;
  color: var(--ed-text-2);
}

.gpu-prop dd {
  margin: 0;
  font-weight: 600;
  color: var(--ed-text-1);
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
