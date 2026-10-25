<!-- Free and libre open source: the license, the repository, and the places to talk and install. -->
<script setup lang="ts">
import { computed } from 'vue'
import { useData } from 'vitepress'
import { brIconUrl, iconUrl } from './icons'
import type { ThemeConfig } from '../config'

const { theme } = useData<ThemeConfig>()
const godotVersion = computed(() => theme.value.godotVersion ?? '')

const repo = 'https://github.com/outfox/2dog'
const links = [
  { text: 'Browse the source', href: repo, icon: 'code' },
  { text: 'Report an issue', href: `${repo}/issues`, icon: 'bug' },
  { text: 'Meet the contributors', href: `${repo}/graphs/contributors`, icon: 'happy_face' },
]
</script>

<template>
  <section class="home-section alt" id="open-source" aria-labelledby="free-title">
    <div class="home-wrap free-grid">
      <div>
        <p class="home-kicker">
          <span class="gd-icon free-heart" :style="{ '--gd-icon': iconUrl('heart') }" aria-hidden="true"></span>Open source
        </p>
        <h2 id="free-title" class="home-h2">Free &amp; libre, as Godot itself.<a class="home-anchor" href="#open-source" aria-label="Permalink to this section">#</a></h2>
        <p class="home-intro">
          2dog is MIT licensed. Use it in commercial games, fork it, read every line, throw us a <s>ball</s> patch. It's just a good dog.
        </p>
        <div class="home-actions free-actions">
          <a class="home-btn primary" :href="repo">
            <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('github') }" aria-hidden="true"></span>
            Star 2dog on GitHub
          </a>
          <a class="home-btn" href="https://discord.com/invite/GAXdbZCNGT">
            <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('discord') }" aria-hidden="true"></span>
            Chat on Discord
          </a>
        </div>
      </div>

      <div class="repo-card">
        <a class="repo-name" :href="repo">
          <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('github') }" aria-hidden="true"></span>
          outfox/2dog
        </a>
        <p class="repo-desc">Godot as a library for .NET: hosts, templates, test fixtures and a CLI.</p>
        <ul class="repo-meta" aria-label="Repository facts">
          <li><span class="repo-dot lic" aria-hidden="true"></span>MIT License</li>
          <li><span class="repo-dot cs" aria-hidden="true"></span>C#</li>
          <li>Godot {{ godotVersion }}</li>
        </ul>
        <ul class="repo-links">
          <li v-for="l in links" :key="l.href">
            <a :href="l.href">
              <span class="gd-icon" :style="{ '--gd-icon': iconUrl(l.icon) }" aria-hidden="true"></span>
              {{ l.text }}
            </a>
          </li>
          <li>
            <a href="https://www.nuget.org/packages/2dog">
              <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('nuget') }" aria-hidden="true"></span>
              Install from NuGet
            </a>
          </li>
        </ul>
      </div>
    </div>
  </section>
</template>

<style>
.free-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr);
  gap: 64px;
  align-items: center;
}

.free-actions {
  margin-top: 32px;
}

.repo-card {
  padding: 28px;
  border: 1px solid var(--ed-seam);
  border-radius: 12px;
  background: var(--ed-base);
}

.repo-name {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  font-family: var(--vp-font-family-mono), monospace;
  font-size: 20px;
  font-weight: 400;
  color: var(--ed-text-1);
  text-decoration: none;
}

.repo-name:hover {
  color: var(--ed-accent);
}

.repo-name .brand-glyph {
  width: 26px;
  height: 26px;
  color: var(--ed-text-1);
}

.repo-desc {
  margin: 12px 0 0;
  color: var(--ed-text-2);
}

.repo-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 20px;
  margin: 16px 0 0;
  padding: 0 0 20px;
  border-bottom: 1px solid var(--ed-seam);
  list-style: none;
  font-size: 15px;
  color: var(--ed-text-2);
}

.repo-meta li {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.repo-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
}

.repo-dot.lic {
  background: var(--hue-amber);
}

.home-kicker .gd-icon.free-heart {
  background-color: var(--ed-spark);
}

.repo-dot.cs {
  background: var(--id-dotnet);
}

.repo-links {
  margin: 12px 0 0;
  padding: 0;
  list-style: none;
}

.repo-links a {
  display: flex;
  align-items: center;
  gap: 12px;
  min-height: 44px;
  font-weight: 600;
  color: var(--ed-accent);
  text-decoration: none;
}

.repo-links a:hover {
  text-decoration: underline;
}

.repo-links .gd-icon,
.repo-links .brand-glyph {
  width: 16px;
  height: 16px;
}

@media (max-width: 959px) {
  .free-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 40px;
  }
}

@media (max-width: 719px) {
  .repo-card {
    padding: 20px;
  }
}
</style>
