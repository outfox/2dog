<!-- Editor menus: real dropdowns in the editor's own anatomy — separator groups,
     right-aligned shortcut hints, and greyed filler rows for the full-menu illusion.
     Real rows link to actual doc pages; disabled rows are inert but announced.
     This strip is the home page's whole navigation — the site header stands down
     there (custom.css) — so search and the appearance switch live here too.
     Godot's runbar rides the strip's right edge. -->
<script setup lang="ts">
import { computed, ref, nextTick, onMounted, onUnmounted } from 'vue'
import { useData, withBase } from 'vitepress'
import { iconUrl, edIconUrl, brIconUrl } from './icons'

const { isDark } = useData()

type MenuItem = {
  text?: string
  link?: string
  shortcut?: string
  disabled?: boolean
  action?: 'search' | 'theme'
  sep?: boolean
  icon?: string
}

/* Computed so the theme row can name the appearance it switches to. Popups render
   only while open, so this never reaches SSR and cannot mismatch on hydration. */
const menus = computed<{ label: string; items: MenuItem[] }[]>(() => [
  {
    label: 'Scene',
    items: [
      { text: 'New Scene', disabled: true },
      { text: 'New Inherited Scene...', disabled: true, shortcut: 'Ctrl+Shift+N' },
      { text: 'Open Scene...', disabled: true, shortcut: 'Ctrl+O' },
      { text: 'Reopen Closed Scene', disabled: true, shortcut: 'Ctrl+Shift+T' },
      { sep: true },
      { text: 'Save Scene', disabled: true, shortcut: 'Ctrl+S' },
      { text: 'Save All Scenes', disabled: true, shortcut: 'Ctrl+Alt+S' },
      { sep: true },
      { text: 'Quit', disabled: true, shortcut: 'Ctrl+Q' },
    ],
  },
  {
    label: 'Project',
    items: [
      { text: 'New Project...', link: '/templates', shortcut: 'Ctrl+N' },
      { text: 'Convert Project...', link: '/convert', shortcut: 'Ctrl+Shift+C' },
      { sep: true },
      { text: 'Getting Started', link: '/getting-started' },
      { text: 'MSBuild Configuration', link: '/configuration' },
      { text: 'Resource Import', link: '/import-tool' },
      { sep: true },
      { text: 'Project Settings...', disabled: true },
      { text: 'Export...', disabled: true },
      { text: 'Pack Project as ZIP...', disabled: true },
      { text: 'Reload Current Project', disabled: true },
    ],
  },
  {
    label: 'Debug',
    items: [
      { text: 'Testing with xUnit', link: '/testing' },
      { text: 'Known Issues', link: '/known-issues/' },
      { text: 'GD.Print in Tests', link: '/known-issues/gd-print-output' },
      { sep: true },
      { text: 'Deploy with Remote Debug', disabled: true },
      { text: 'Visible Collision Shapes', disabled: true },
      { text: 'Synchronize Scene Changes', disabled: true },
    ],
  },
  {
    label: 'Editor',
    items: [
      { text: 'Build Variants', link: '/build-configurations' },
      { text: 'Web / Browser (WASM)', link: '/web' },
      { text: 'API Reference', link: '/api-reference' },
      { sep: true },
      {
        text: isDark.value ? 'Use Light Theme' : 'Use Dark Theme',
        action: 'theme',
        icon: iconUrl(isDark.value ? 'sun' : 'moon'),
      },
      { text: 'Editor Settings...', disabled: true },
      { text: 'Editor Layout', disabled: true },
      { text: 'Take Screenshot', disabled: true, shortcut: 'Ctrl+F12' },
      { text: 'Toggle Fullscreen', disabled: true, shortcut: 'Shift+F11' },
    ],
  },
  {
    label: 'Help',
    items: [
      { text: 'Search Help...', action: 'search', shortcut: 'Ctrl+K', icon: edIconUrl('HelpSearch') },
      { sep: true },
      { text: 'FAQ', link: '/faq', icon: iconUrl('speech_bubble_question') },
      { text: 'Dog Park (Discord)', link: 'https://discord.gg/GAXdbZCNGT', icon: brIconUrl('discord') },
      { text: 'Fetch (GitHub)', link: 'https://github.com/outfox/2dog', icon: brIconUrl('github') },
      { sep: true },
      { text: 'About Godot...', disabled: true, icon: edIconUrl('GodotMonochrome') },
      { text: 'Copy System Info', disabled: true, icon: edIconUrl('ActionCopy') },
    ],
  },
])

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

/* Search Help genuinely opens the site search (its Ctrl+K works everywhere). The
   nav is hidden on this page, not unmounted, so its button is still clickable. */
function searchHelp() {
  openMenu.value = null
  const btn = document.querySelector<HTMLElement>('.VPNavBarSearchButton, .DocSearch-Button')
  btn?.click()
}

/* The appearance switch lives here because the site header is hidden on the home
   page — and the Editor menu is where a Godot user goes looking for it anyway. */
function toggleTheme() {
  isDark.value = !isDark.value
  openMenu.value = null
}

function runAction(it: MenuItem) {
  if (it.action === 'search') searchHelp()
  else if (it.action === 'theme') toggleTheme()
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
      wrap.querySelector<HTMLButtonElement>('.ed-menu-btn')?.focus()
    }
    return
  }
  if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
  e.preventDefault()
  const focusables = () =>
    [...wrap.querySelectorAll<HTMLElement>('.ed-menu-pop a, .ed-menu-pop button')]
  if (openMenu.value !== label) {
    openMenu.value = label
    void nextTick(() => focusables()[0]?.focus())
    return
  }
  const links = focusables()
  const i = links.indexOf(document.activeElement as HTMLElement)
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
        <template v-for="(it, i) in m.items" :key="i">
          <span v-if="it.sep" class="ed-menu-sep" aria-hidden="true"></span>
          <!-- Filler rows are inert but announced as unavailable, not hidden: a menu
               whose entries are all disabled must still tell you what it holds. -->
          <span v-else-if="it.disabled" class="ed-menu-item is-disabled" aria-disabled="true">
            <span v-if="it.icon" class="ed-menu-glyph" :style="{ '--gd-icon': it.icon }" aria-hidden="true"></span>
            {{ it.text }}<span v-if="it.shortcut" class="ed-menu-key" aria-hidden="true">{{ it.shortcut }}</span>
          </span>
          <button v-else-if="it.action" type="button" class="ed-menu-item" @click="runAction(it)">
            <span v-if="it.icon" class="ed-menu-glyph" :style="{ '--gd-icon': it.icon }" aria-hidden="true"></span>
            {{ it.text }}<span v-if="it.shortcut" class="ed-menu-key" aria-hidden="true">{{ it.shortcut }}</span>
          </button>
          <a v-else class="ed-menu-item" :href="menuHref(it.link!)">
            <span v-if="it.icon" class="ed-menu-glyph" :style="{ '--gd-icon': it.icon }" aria-hidden="true"></span>
            {{ it.text }}<span v-if="it.shortcut" class="ed-menu-key" aria-hidden="true">{{ it.shortcut }}</span>
          </a>
        </template>
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
  min-width: 260px;
  padding: 4px;
  background: var(--ed-base);
  border: 1px solid var(--ed-seam);
  border-radius: 3px;
  box-shadow: 0 8px 24px -8px rgba(0, 0, 0, 0.55);
}

/* One row grammar for links, actions, and greyed filler. */
.ed-menu-item {
  display: flex;
  align-items: center;
  width: 100%;
  padding: 5px 10px;
  border: none;
  border-radius: 3px;
  background: transparent;
  font-size: 12.5px;
  text-align: left;
  color: var(--ed-text-1);
  text-decoration: none;
  white-space: nowrap;
  cursor: pointer;
}

.ed-menu-item:hover {
  background: var(--ed-raise);
  color: var(--ed-accent);
}

.ed-menu-item:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

/* Leading glyphs (Help menu): mask-tinted, following the row's text color. */
.ed-menu-glyph {
  flex: none;
  width: 14px;
  height: 14px;
  margin-right: 8px;
  background-color: currentColor;
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

/* The shortcut column, dim and right-aligned like the editor's. */
.ed-menu-key {
  margin-left: auto;
  padding-left: 28px;
  font-size: 11.5px;
  color: var(--ed-text-3);
}

/* Greyed rows: present for the illusion, inert for everyone. */
.ed-menu-item.is-disabled {
  color: var(--ed-text-3);
  opacity: 0.65;
  cursor: default;
}

.ed-menu-item.is-disabled:hover {
  background: transparent;
  color: var(--ed-text-3);
}

.ed-menu-sep {
  display: block;
  height: 1px;
  margin: 4px 6px;
  background: var(--ed-seam);
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
