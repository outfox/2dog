<!--
THESIS: The docs are a Godot editor session visibly running inside a .NET host window — the ownership
inversion rendered as chrome. Refuses the category default: gradient wordmark hero over three feature cards.
OWN-WORLD: Godot editor dark theme from source — #242424 ground, #1c1c1c/#161616 panels, 1px #0f0f0f seams,
#579eff accent, node colors #8da5f3/#fc7f7f/#8eef97, Inter UI + JetBrains Mono machine text; docks, tree
items, tabs, console lines — never web cards.
STORY: A Godot C# dev recognizes their editor instantly, reads the title bar, and understands without a
word that .NET owns the process; the console proves it with real commands; they click Read the Dogs.
FIRST VIEWPORT: host title bar on top; scene-tree dock (real host projects, real links) left; center
viewport with the running dog, headline, two actions; inspector dock (the four product claims as
property categories) right; output console streaming pun log lines below. Nothing outside the window.
FORM: Godot editor interface grammar — grounded candidate 4, seed 93cdfc31; staging: the editor's native
dock layout (dealt "summoned definitions" staging not adopted).
-->
<script setup lang="ts">
import { ref, computed, nextTick, onMounted, onUnmounted } from 'vue'
import { useData, withBase } from 'vitepress'

const { theme, isDark } = useData()
const twodogVersion = computed(() => (theme.value as any).twodogVersion ?? '')

/* The retired hero carousel's pun inventory, evolved into console log material. */
const puns: string[] = [
  'What if Godot... but backward?',
  "Let's take Godot for walkies!",
  "Who's a good engine? Godot is! Yes it is!",
  'Sit, Godot! Good engine. Now render.',
  'Teaching old Godot new tricks',
  'Godot? More like Go-do-what-you-want!',
  'Every Robot deserves a good .NET home',
  'Fetch the scene tree! Good Robot!',
  'No more waiting for Godot',
  'Your Godot, your rules',
  'Godot heel! Godot speak! Godot run!',
  'Roll over, Godot! Time to run .NET side up',
  'Unit tests? A walk in the park!',
  'MIT Licensed Service Dog — free to good home!',
  "We put the 'woof' in workflow!",
]

const punLine = ref('')
const punTarget = ref(puns[Math.floor(Math.random() * puns.length)])
let typeTimer: number | undefined
let rotateTimer: number | undefined
let reducedMotion = false

function typeTowards() {
  if (reducedMotion) {
    punLine.value = punTarget.value
    return
  }
  if (punLine.value.length < punTarget.value.length) {
    punLine.value = punTarget.value.slice(0, punLine.value.length + 1)
    typeTimer = window.setTimeout(typeTowards, 24)
  }
}

function nextPun() {
  let next = punTarget.value
  while (next === punTarget.value) {
    next = puns[Math.floor(Math.random() * puns.length)]
  }
  punTarget.value = next
  punLine.value = ''
  typeTowards()
}

/* Rotate only when motion is welcome and the tab is visible; one pun is plenty otherwise. */
function onVisibility() {
  window.clearInterval(rotateTimer)
  if (!document.hidden) {
    rotateTimer = window.setInterval(nextPun, 6000)
  }
}

onMounted(() => {
  reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  typeTowards()
  if (!reducedMotion) {
    rotateTimer = window.setInterval(nextPun, 6000)
    document.addEventListener('visibilitychange', onVisibility)
  }
  document.addEventListener('click', onDocClick)
})

onUnmounted(() => {
  window.clearTimeout(typeTimer)
  window.clearInterval(rotateTimer)
  document.removeEventListener('visibilitychange', onVisibility)
  document.removeEventListener('click', onDocClick)
})

/* Node glyphs: neutral pictograms from public/icons/node, tinted like Godot's tree. */
const iconUrl = (name: string) => `url(${withBase(`/icons/node/${name}.svg`)})`

/* Godot's runbar as titlebar chrome. Play is lit: the scene — the dog — is running. */
const runbar = [
  { icon: 'hammer' },
  { icon: 'play', active: true },
  { icon: 'pause' },
  { icon: 'square' },
  { icon: 'clapperboard' },
  { icon: 'film_camera' },
  { icon: 'film' },
]

const hosts = [
  { name: 'MyGame.2dog', role: 'Desktop host', color: 'var(--ed-node-2d)', icon: 'window', link: '/project-layout' },
  { name: 'MyGame.web', role: 'Browser host', color: 'var(--ed-node-gui)', icon: 'globe', link: '/web' },
  { name: 'MyGame.tests', role: 'xUnit host', color: 'var(--ed-node-gold)', icon: 'test_tube', link: '/testing' },
]

/* Inspector dock: the four product claims as property categories of the running scene. */
const features = [
  {
    icon: 'globe',
    tint: '2d',
    name: 'C# on the Web',
    detail: 'Yay, C# Games in HTML5 and WASM. No more waiting for Godot!',
    link: '/convert',
    linkText: 'Bring your project',
  },
  {
    icon: 'joystick',
    tint: '3d',
    name: 'This is still Godot',
    detail: 'A companion, not a replacement? 100% compatible, switch as you like.',
    link: '/project-layout',
    linkText: 'See what changes',
  },
  {
    icon: 'test_tube',
    tint: 'gui',
    name: 'Test like a Pro',
    detail: 'Test scenes, scripts, or resources in xUnit. In standard IDE test runners!',
    link: '/testing',
    linkText: 'Test a scene',
  },
  {
    icon: 'curly_brackets',
    tint: 'gold',
    name: '.NET holds the Leash',
    detail: 'Your process starts Godot, pumps each frame, and controls the engine.',
    link: '/concepts',
    linkText: 'How that works',
  },
]

/* Editor menus: real dropdowns listing actual doc pages by name — the fiction, one interaction deeper. */
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

/* Bottom panel: Start Here (the two-path choice, as docked subwindows) + Output. */
const activeTab = ref<'start' | 'output'>('start')

function tabsKeydown(e: KeyboardEvent) {
  if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
  e.preventDefault()
  activeTab.value = activeTab.value === 'start' ? 'output' : 'start'
  void nextTick(() =>
    document.getElementById(activeTab.value === 'start' ? 'tab-start' : 'tab-output')?.focus()
  )
}

const copied = ref<string | null>(null)

async function copyCommands(s: (typeof starts)[number]) {
  const text = s.code.filter((l) => !l.comment).map((l) => l.text).join('\n')
  try {
    await navigator.clipboard.writeText(text)
    copied.value = s.link
    window.setTimeout(() => {
      if (copied.value === s.link) copied.value = null
    }, 2000)
  } catch {
    /* clipboard unavailable (permissions/insecure context) — button simply stays "Copy" */
  }
}

const starts = [
  {
    icon: 'arrow_right_arrow_left',
    tint: 'gold',
    title: 'I Have a Godot Game',
    //body: 'Just add 2dog! Scenes, scripts, and assets stay as they are.',
    code: [
      { comment: true, text: '# just add 2dog! scenes/scripts/assets stay as they are.' },
      { comment: true, text: '# no install needed, just use dnx (dotnet tool execute)' },
      { comment: false, text: 'dnx 2dog convert path/to/MyGame' },
      { comment: false, text: 'cd path/to/MyGame' },
      { comment: false, text: 'dotnet run --project MyGame.2dog' },
    ],
    link: '/convert',
    linkText: 'What conversion adds →',
  },
  {
    icon: 'tennis_ball',
    tint: 'gui',
    title: "I'm Starting Fresh",
    //body: 'Creates a sample scene plus every host, ready to run, test, and publish.',
    code: [
      { comment: true, text: '# spawn an empty project with every host you can run/test/publish.' },
      { comment: false, text: 'dotnet new install 2dog' },
      { comment: false, text: 'dotnet new 2dog -n MyGame' },
      { comment: false, text: 'cd MyGame' },
      { comment: false, text: 'dotnet run --project MyGame.2dog' },
    ],
    link: '/templates',
    linkText: 'Template options →',
  },
]
</script>

<template>
  <section class="editor-home" aria-label="2dog: start, control, and embed Godot in .NET">
    <div class="host-window">
      <!-- The .NET process owns this window; Godot runs inside it. -->
      <div class="host-titlebar">
        <span class="host-proc"><span
          class="host-app-icon"
          :style="{ '--gd-icon': iconUrl('gobot') }"
          aria-hidden="true"
        ></span>MyGame.tscn — 2dog</span>
        <span class="host-runbar" aria-hidden="true">
          <span
            v-for="r in runbar"
            :key="r.icon"
            class="run-glyph"
            :class="{ active: r.active }"
            :style="{ '--gd-icon': iconUrl(r.icon) }"
          ></span>
        </span>
        <span class="host-controls" aria-hidden="true"><i>–</i><i>□</i><i>×</i></span>
      </div>

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
        <span class="ed-menubar-note" aria-hidden="true">(hosted by .NET — everything still works)</span>
      </nav>

      <div class="ed-body">
        <nav class="ed-tree" aria-label="Project hosts">
          <div class="ed-dock-title">Scene</div>
          <a class="ed-tree-root" :href="withBase('/project-layout')">
            <span class="node-glyph root" :style="{ '--gd-icon': iconUrl('joystick') }" aria-hidden="true"></span>MyGame
            <span class="ed-tree-note">project.godot</span>
          </a>
          <a
            v-for="h in hosts"
            :key="h.name"
            class="ed-tree-item"
            :href="withBase(h.link)"
          >
            <span
              class="node-glyph"
              :style="{ backgroundColor: h.color, '--gd-icon': iconUrl(h.icon) }"
              aria-hidden="true"
            ></span>
            <span class="ed-tree-name">{{ h.name }}</span>
            <span class="ed-tree-note">{{ h.role }}</span>
          </a>
        </nav>

        <div class="ed-viewport">
          <img
            class="ed-mascot"
            :src="withBase('/logo-animated.svg')"
            alt="2dog logotype: a happy white dog over the word 2dog"
            width="300"
            height="154"
          />
          <h1 class="ed-headline">Start, control, embed Godot in&nbsp;.NET.</h1>
          <p class="ed-tagline">
            Keep your scenes, scripts, and editor workflow.<br />
            Gain an entire ecosystem.
          </p>
          <div class="ed-actions">
            <a class="ed-btn brand" :href="withBase('/getting-started')">Read the Dogs</a>
            <a class="ed-btn" href="https://github.com/outfox/2dog">Fetch on GitHub</a>
          </div>
        </div>

        <aside class="ed-inspector" aria-label="Why 2dog">
          <div class="ed-dock-title">Inspector</div>
          <section v-for="f in features" :key="f.link" class="ed-prop">
            <h2 class="ed-prop-title">
              <span
                :class="['gd-icon', `gd-icon--${f.tint}`]"
                :style="{ '--gd-icon': iconUrl(f.icon) }"
                aria-hidden="true"
              ></span>
              {{ f.name }}
            </h2>
            <p class="ed-prop-detail">{{ f.detail }}</p>
            <a class="ed-prop-link" :href="withBase(f.link)">{{ f.linkText }} →</a>
          </section>
        </aside>
      </div>

      <div class="ed-bottom">
        <!-- Tablist first in DOM for focus order; CSS `order` keeps it visually at the
             bottom, where Godot puts its bottom-panel tab strip. The typing pun rides
             the strip as a persistent status line; version at the right edge. -->
        <div class="ed-console-tabs" role="tablist" aria-label="Bottom panel" @keydown="tabsKeydown">
          <button
            id="tab-start"
            class="ed-console-tab"
            :class="{ active: activeTab === 'start' }"
            role="tab"
            aria-controls="panel-start"
            :aria-selected="activeTab === 'start'"
            :tabindex="activeTab === 'start' ? 0 : -1"
            @click="activeTab = 'start'"
          >Quickstart</button>
          <button
            id="tab-output"
            class="ed-console-tab"
            :class="{ active: activeTab === 'output' }"
            role="tab"
            aria-controls="panel-output"
            :aria-selected="activeTab === 'output'"
            :tabindex="activeTab === 'output' ? 0 : -1"
            @click="activeTab = 'output'"
          >Output</button>
          <p class="ed-status-pun" aria-live="off"><span
            class="gd-icon gd-icon--dim ln-glyph"
            :style="{ '--gd-icon': iconUrl('paw_print') }"
            aria-hidden="true"
          ></span> {{ punLine }}<span class="caret" aria-hidden="true"></span></p>
          <a v-if="twodogVersion" class="ed-console-ver" href="https://github.com/outfox/2dog"><span
            class="gd-icon gd-icon--gold ln-glyph"
            :style="{ '--gd-icon': iconUrl('bone') }"
            aria-hidden="true"
          ></span> 2dog {{ twodogVersion }}</a>
        </div>

        <!-- Both panels share one grid cell: the tallest reserves the height,
             so switching tabs never resizes the host window. -->
        <div class="ed-panels">
        <div
          id="panel-start"
          class="ed-start"
          :class="{ 'is-hidden': activeTab !== 'start' }"
          role="tabpanel"
          aria-labelledby="tab-start"
        >
          <div class="ed-subwins">
            <article v-for="s in starts" :key="s.link" class="ed-subwin">
              <h2 class="ed-subwin-title">
                <span
                  :class="['gd-icon', `gd-icon--${s.tint}`]"
                  :style="{ '--gd-icon': iconUrl(s.icon) }"
                  aria-hidden="true"
                ></span>
                {{ s.title }}
              </h2>
              <div class="ed-subwin-body">
                <p>{{ s.body }}</p>
                <div class="ed-shell-wrap">
                  <pre class="ed-shell"><code><span
                    v-for="(l, i) in s.code"
                    :key="i"
                    :class="{ comment: l.comment }"
                  >{{ l.text }}
</span></code></pre>
                  <button
                    type="button"
                    class="ed-copy"
                    :aria-label="`Copy commands: ${s.title}`"
                    @click="copyCommands(s)"
                  >{{ copied === s.link ? 'Copied!' : 'Copy' }}</button>
                </div>
                <a class="ed-subwin-link" :href="withBase(s.link)">{{ s.linkText }}</a>
              </div>
            </article>
          </div>
        </div>

        <div
          id="panel-output"
          class="ed-output"
          :class="{ 'is-hidden': activeTab !== 'output' }"
          role="tabpanel"
          aria-labelledby="tab-output"
        >
          <!-- A real desktop-host run, adapted to MyGame. -->
          <div class="ed-console-lines">
            <p class="ln sys">Starting Godot instance...</p>
            <p class="ln">Engine: Godot instance created successfully!</p>
            <p class="ln sys">Godot Engine (2dog) v4.7.1.stable.mono.2dog.7200b53af (2026-07-22 21:45:57 UTC) - https://2dog.dev</p>
            <p class="ln sys">Vulkan 1.4.341 - Forward+ - Using Device #0: NVIDIA - NVIDIA GeForce RTX 3080</p>
            <p class="ln" aria-hidden="true">&nbsp;</p>
            <p class="ln">Engine: Godot started successfully!</p>
            <p class="ln ok">2dog is running 'MyGame'!</p>
            <p class="ln sys">Close the window to quit.</p>
            <p class="ln sys">Shutting down...</p>
            <p class="ln">Engine: Godot instance destroyed.</p>
          </div>
        </div>

        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
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

/* Godot's runbar, docked at the titlebar's right like the editor's own. */
.host-runbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-left: auto;
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

.host-controls {
  display: flex;
  gap: 10px;
  color: var(--ed-text-2);
  font-style: normal;
}

.host-controls i {
  font-style: normal;
}

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

.ed-menubar-note {
  margin-left: auto;
  padding-right: 6px;
  font-size: 11.5px;
  color: var(--ed-text-2);
}

.ed-body {
  display: grid;
  grid-template-columns: 232px 1fr 260px;
}

/* Inspector dock: the product claims as property categories, Godot's own right-dock placement. */
.ed-inspector {
  background: var(--ed-dark-1);
  border-left: 1px solid var(--ed-seam);
  padding: 8px 8px 12px;
}

.ed-prop-title {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  margin: 0;
  padding: 5px 8px;
  border-radius: 3px;
  background: var(--ed-raise);
  font-size: 12.5px;
  font-weight: 500;
  line-height: 1.4;
  color: var(--ed-text-1);
}

.ed-prop-title .gd-icon {
  flex: none;
  width: 14px;
  height: 14px;
}

.ed-prop-detail {
  margin: 7px 8px 3px;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--ed-text-2);
}

.ed-prop-link {
  display: block;
  margin: 0 8px 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--ed-accent);
  text-decoration: none;
}

.ed-prop-link:hover {
  color: var(--ed-accent-strong);
}

.ed-prop-link:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: 2px;
}

/* Scene-tree dock: the real host projects, as real links. */
.ed-tree {
  background: var(--ed-dark-1);
  border-right: 1px solid var(--ed-seam);
  padding: 8px 8px 12px;
}

.ed-dock-title {
  font-family: var(--vp-font-family-mono);
  font-size: 11px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--ed-text-2);
  padding: 4px 8px 8px;
}

.ed-tree-root,
.ed-tree-item {
  display: flex;
  align-items: baseline;
  gap: 8px;
  padding: 5px 8px;
  border-radius: 3px;
  font-size: 13px;
  color: var(--ed-text-1);
  text-decoration: none;
}

.ed-tree-item {
  margin-left: 14px;
  border-left: 1px solid var(--ed-seam);
  border-radius: 0 3px 3px 0;
}

.ed-tree-root:hover,
.ed-tree-item:hover {
  background: var(--ed-raise);
  color: var(--ed-accent);
}

.node-glyph {
  flex: none;
  width: 13px;
  height: 13px;
  align-self: center;
  background-color: var(--ed-text-2);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

.node-glyph.root {
  background-color: var(--ed-accent);
}

.ed-tree-name {
  font-family: var(--vp-font-family-mono);
  font-size: 12.5px;
}

.ed-tree-note {
  margin-left: auto;
  font-size: 11px;
  color: var(--ed-text-2);
}

/* Viewport: the 2D editor's grid, with the current scene running. */
.ed-viewport {
  position: relative;
  padding: 40px 40px 44px;
  background:
    linear-gradient(var(--ed-grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--ed-grid) 1px, transparent 1px),
    var(--ed-dark-2);
  background-size: 32px 32px, 32px 32px, auto;
  text-align: center;
}

/* The SVG animates itself (with its own reduced-motion guard); no outer float to compound it. */
.ed-mascot {
  width: min(300px, 60%);
  height: auto;
  margin: 0 auto 8px;
}

@media (prefers-reduced-motion: reduce) {
  .caret { animation: none; }
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

/* Bottom panel, per the editor itself: content wells above, tab strip at the bottom,
   everything flowing on one ground — no window chrome, no shadows, no seams inside. */
.ed-bottom {
  display: flex;
  flex-direction: column;
  border-top: 1px solid var(--ed-seam);
  background: var(--ed-base);
}

/* The typing pun as a status-bar line: the authored moment, visible by default. */
.ed-status-pun {
  flex: 1;
  min-width: 0;
  margin: 0 0 0 12px;
  font-family: var(--vp-font-family-mono);
  font-size: 11.5px;
  color: var(--ed-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ed-panels {
  display: grid;
}

.ed-panels > * {
  grid-area: 1 / 1;
}

.ed-panels > .is-hidden {
  visibility: hidden;
}

/* First in DOM (focus order) but rendered at the bottom via `order`, Godot-style. */
.ed-console-tabs {
  order: 2;
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 3px 8px;
}

.ed-console-tab {
  padding: 5px 13px;
  border: none;
  border-radius: 3px;
  background: transparent;
  font-size: 12.5px;
  color: var(--ed-text-2);
  cursor: pointer;
}

.ed-console-tab:hover {
  color: var(--ed-text-1);
}

.ed-console-tab:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

.ed-console-tab.active {
  background: var(--ed-raise);
  color: var(--ed-text-1);
}

/* Start Here panel: the choice as two inset wells, Stack Frames / Breakpoints style. */
.ed-start {
  display: flex;
  flex-direction: column;
  padding: 6px 6px 3px;
}

/* Wells stretch to the panel height the taller Output transcript reserves. */
.ed-subwins {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 6px;
}

.ed-subwin {
  display: flex;
  flex-direction: column;
  border-radius: 3px;
  background: var(--ed-dark-1);
  overflow: hidden;
}

.ed-subwin-title {
  margin: 0;
  padding: 6px 12px;
  background: var(--ed-raise);
  font-size: 12.5px;
  font-weight: 500;
  line-height: 1.4;
  text-align: center;
  color: var(--ed-text-1);
}

.ed-subwin-title .gd-icon {
  width: 14px;
  height: 14px;
  margin-right: 3px;
  vertical-align: -2.5px;
}

.ed-subwin-body {
  display: flex;
  flex-direction: column;
  flex: 1;
  padding: 12px 14px 12px;
}

.ed-subwin-body > p {
  margin: 0;
  font-size: 13.5px;
  line-height: 1.6;
  color: var(--ed-text-1);
}

.ed-shell-wrap {
  position: relative;
  margin: 12px 0 0;
}

.ed-shell {
  margin: 0;
  padding: 10px 12px;
  border-radius: 3px;
  background: var(--ed-dark-2);
  overflow-x: auto;
}

/* Hover-revealed editor button, like the site's code blocks. */
.ed-copy {
  position: absolute;
  top: 6px;
  right: 6px;
  padding: 3px 9px;
  border: 1px solid var(--ed-seam);
  border-radius: 3px;
  background: var(--ed-raise);
  font-size: 11px;
  color: var(--ed-text-2);
  cursor: pointer;
  opacity: 0;
  transition: opacity 0.1s ease-out, color 0.1s ease-out;
}

.ed-shell-wrap:hover .ed-copy,
.ed-copy:focus-visible {
  opacity: 1;
}

.ed-copy:hover {
  color: var(--ed-text-1);
  border-color: var(--ed-accent);
}

.ed-copy:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: 2px;
}

.ed-shell code {
  font-family: var(--vp-font-family-mono);
  font-size: 12.5px;
  line-height: 1.75;
  color: var(--ed-text-1);
  white-space: pre;
}

.ed-shell .comment {
  color: var(--ed-text-2);
}

.ed-subwin-link {
  margin-top: auto;
  padding-top: 12px;
  font-size: 13px;
  font-weight: 500;
  color: var(--ed-accent);
  text-decoration: none;
}

.ed-subwin-link:hover {
  color: var(--ed-accent-strong);
}

.ed-subwin-link:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: 2px;
}

.ed-console-ver {
  margin-left: auto;
  padding-right: 6px;
  font-family: var(--vp-font-family-mono);
  font-size: 11px;
  color: var(--ed-text-2);
  text-decoration: none;
}

.ed-console-ver:hover {
  color: var(--ed-text-1);
}

.ed-console-ver:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
  border-radius: 3px;
}

.ed-output {
  display: flex;
  flex-direction: column;
  padding: 6px 6px 3px;
}

.ed-console-lines {
  flex: 1;
  padding: 8px 12px 10px;
  border-radius: 3px;
  background: var(--ed-dark-2);
  font-family: var(--vp-font-family-mono);
  font-size: 12.5px;
  line-height: 1.7;
}

.ln {
  margin: 0;
  color: var(--ed-text-1);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ln.sys {
  color: var(--ed-text-2);
}

.ln.ok {
  color: var(--ed-success);
}

.ln-glyph {
  width: 13px;
  height: 13px;
  vertical-align: -2px;
}

.caret {
  display: inline-block;
  width: 7px;
  height: 12px;
  margin-left: 2px;
  vertical-align: -2px;
  background: var(--ed-accent);
  animation: caret-blink 1.1s steps(1) infinite;
}

@keyframes caret-blink {
  50% { opacity: 0; }
}

/* ---------- Responsive: docks stack ---------- */

/* Narrow: the inspector redocks below the viewport, as Godot does when docks collapse. */
@media (max-width: 1139px) {
  .ed-body {
    grid-template-columns: 232px 1fr;
  }

  .ed-inspector {
    grid-column: 1 / -1;
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    column-gap: 10px;
    align-items: start;
    border-left: none;
    border-top: 1px solid var(--ed-seam);
  }

  .ed-inspector .ed-dock-title {
    grid-column: 1 / -1;
  }
}

@media (max-width: 819px) {
  .editor-home {
    padding: 20px 16px 4px;
  }

  .ed-body {
    grid-template-columns: 1fr;
  }

  .ed-tree {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 2px;
    border-right: none;
    border-bottom: 1px solid var(--ed-seam);
    padding: 6px 8px;
  }

  .ed-dock-title {
    padding: 4px 6px;
  }

  .ed-tree-item {
    margin-left: 0;
    border-left: none;
  }

  .ed-tree-note {
    display: none;
  }

  .ed-inspector {
    grid-template-columns: 1fr;
  }

  .host-runbar {
    display: none;
  }

  .ed-menubar {
    flex-wrap: wrap;
  }

  .ed-menubar-note {
    display: none;
  }

  .ed-status-pun {
    display: none;
  }

  .ed-shell code {
    font-size: 11.5px;
  }

  .ed-viewport {
    padding: 28px 20px 32px;
  }
}
</style>
