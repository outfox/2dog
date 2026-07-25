<!-- Editor menus: real dropdowns listing actual doc pages by name — the fiction,
     one interaction deeper. Godot's runbar rides the strip's right edge. -->
<script setup lang="ts">
import { ref, nextTick, onMounted, onUnmounted } from 'vue'
import { withBase } from 'vitepress'
import { iconUrl } from './icons'

const menus = [
  {
    label: 'Scene',
    items: [
      { text: 'Project Layout', link: '/project-layout' },
      { text: 'Converting a Godot Project', link: '/convert' },
      { text: 'Creating a New Project', link: '/templates' },
    ],
  },
  {
    label: 'Project',
    items: [
      { text: 'Getting Started', link: '/getting-started' },
      { text: 'MSBuild Configuration', link: '/configuration' },
      { text: 'Resource Import', link: '/import-tool' },
    ],
  },
  {
    label: 'Debug',
    items: [
      { text: 'Testing with xUnit', link: '/testing' },
      { text: 'Known Issues', link: '/known-issues/' },
      { text: 'GD.Print in Tests', link: '/known-issues/gd-print-output' },
    ],
  },
  {
    label: 'Editor',
    items: [
      { text: 'Build Variants', link: '/build-configurations' },
      { text: 'Web / Browser (WASM)', link: '/web' },
      { text: 'API Reference', link: '/api-reference' },
    ],
  },
  {
    label: 'Help',
    items: [
      { text: 'FAQ', link: '/faq' },
      { text: 'Dog Park (Discord)', link: 'https://discord.gg/GAXdbZCNGT' },
      { text: 'Fetch (GitHub)', link: 'https://github.com/outfox/2dog' },
    ],
  },
]

/* The runbar as chrome. Play is lit: the scene — the dog — is running. */
const runbar = [
  { icon: 'hammer' },
  { icon: 'play', active: true },
  { icon: 'pause' },
  { icon: 'square' },
  { icon: 'clapperboard' },
  { icon: 'film_camera' },
  { icon: 'film' },
]

const openMenu = ref<string | null>(null)
const menubarEl = ref<HTMLElement | null>(null)

const menuHref = (link: string) => (link.startsWith('http') ? link : withBase(link))

function toggleMenu(label: string) {
  openMenu.value = openMenu.value === label ? null : label
}

/* Godot behavior: once one menu is open, hovering a sibling opens it. */
function hoverMenu(label: string) {
  if (openMenu.value && openMenu.value !== label) openMenu.value = label
}

function onDocClick(e: MouseEvent) {
  if (openMenu.value && menubarEl.value && !menubarEl.value.contains(e.target as Node)) {
    openMenu.value = null
  }
}

function menuKeydown(e: KeyboardEvent, label: string) {
  const wrap = e.currentTarget as HTMLElement
  if (e.key === 'Escape') {
    if (openMenu.value) {
      openMenu.value = null
      wrap.querySelector<HTMLButtonElement>('button')?.focus()
    }
    return
  }
  if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
  e.preventDefault()
  if (openMenu.value !== label) {
    openMenu.value = label
    void nextTick(() => wrap.querySelector<HTMLAnchorElement>('.ed-menu-pop a')?.focus())
    return
  }
  const links = [...wrap.querySelectorAll<HTMLAnchorElement>('.ed-menu-pop a')]
  const i = links.indexOf(document.activeElement as HTMLAnchorElement)
  const next = e.key === 'ArrowDown' ? (i + 1) % links.length : (i - 1 + links.length) % links.length
  links[next]?.focus()
}

onMounted(() => document.addEventListener('click', onDocClick))
onUnmounted(() => document.removeEventListener('click', onDocClick))
</script>

<template>
  <nav ref="menubarEl" class="ed-menubar" aria-label="Documentation menus">
    <div
      v-for="m in menus"
      :key="m.label"
      class="ed-menu"
      @keydown="menuKeydown($event, m.label)"
    >
      <button
        type="button"
        class="ed-menu-btn"
        :class="{ open: openMenu === m.label }"
        aria-haspopup="true"
        :aria-expanded="openMenu === m.label"
        :aria-controls="`ed-menu-${m.label}`"
        @click="toggleMenu(m.label)"
        @mouseenter="hoverMenu(m.label)"
      >{{ m.label }}</button>
      <div v-if="openMenu === m.label" :id="`ed-menu-${m.label}`" class="ed-menu-pop">
        <a v-for="it in m.items" :key="it.text" :href="menuHref(it.link)">{{ it.text }}</a>
      </div>
    </div>
    <span class="host-runbar" aria-hidden="true">
      <span
        v-for="r in runbar"
        :key="r.icon"
        class="run-glyph"
        :class="{ active: r.active }"
        :style="{ '--gd-icon': iconUrl(r.icon) }"
      ></span>
    </span>
  </nav>
</template>

<style>
.ed-menubar {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 2px 8px;
  background: var(--ed-dark-1);
  border-bottom: 1px solid var(--ed-seam);
  font-size: 12.5px;
}

.ed-menu {
  position: relative;
}

.ed-menu-btn {
  padding: 4px 9px;
  border: none;
  border-radius: 3px;
  background: transparent;
  font-size: 12.5px;
  color: var(--ed-text-1);
  cursor: pointer;
}

.ed-menu-btn:hover,
.ed-menu-btn.open {
  background: var(--ed-raise);
  color: var(--ed-accent);
}

.ed-menu-btn:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

/* Menu popups genuinely float — the one furniture the grammar allows a shadow. */
.ed-menu-pop {
  position: absolute;
  top: calc(100% + 2px);
  left: 0;
  z-index: 30;
  min-width: 220px;
  padding: 4px;
  background: var(--ed-base);
  border: 1px solid var(--ed-seam);
  border-radius: 3px;
  box-shadow: 0 8px 24px -8px rgba(0, 0, 0, 0.55);
}

.ed-menu-pop a {
  display: block;
  padding: 6px 10px;
  border-radius: 3px;
  font-size: 12.5px;
  color: var(--ed-text-1);
  text-decoration: none;
  white-space: nowrap;
}

.ed-menu-pop a:hover {
  background: var(--ed-raise);
  color: var(--ed-accent);
}

.ed-menu-pop a:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

/* Godot's runbar, docked at the menu strip's right like the editor's own. */
.host-runbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-left: auto;
  padding-right: 6px;
}

.run-glyph {
  width: 14px;
  height: 14px;
  background-color: var(--ed-text-2);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

.run-glyph.active {
  background-color: var(--ed-accent);
}

@media (max-width: 819px) {
  .ed-menubar {
    flex-wrap: wrap;
  }

  .host-runbar {
    display: none;
  }
}
</style>
