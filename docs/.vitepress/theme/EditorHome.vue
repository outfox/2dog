<!--
THESIS: The docs are a Godot editor session visibly running inside a .NET host window — the ownership
inversion rendered as chrome. Refuses the category default: gradient wordmark hero over three feature cards.
OWN-WORLD: Godot editor dark theme from source — #242424 ground, #1c1c1c/#161616 panels, 1px #0f0f0f seams,
#579eff accent, node colors #8da5f3/#fc7f7f/#8eef97, Inter UI + JetBrains Mono machine text; docks, tree
items, tabs, console lines — never web cards.
STORY: A Godot C# dev recognizes their editor instantly, reads the title bar, and understands without a
word that .NET owns the process; the console proves it with real commands; they click Read the Dogs.
FIRST VIEWPORT: host title bar on top; Scene + FileSystem docks (real host projects; the game's files,
visibly untouched) left; center viewport with 2D toolbar, the running dog, headline, two actions;
Inspector dock right with real tab strip (Inspector | Signals), toolbar, node selector, a working
property filter, category bars, and a Script row; output console below. Nothing outside the window.
FORM: Godot editor interface grammar — grounded candidate 4, seed 93cdfc31; dock chrome matched against
the shipping 4.7 editor (icon sets: public/icons/godot-editor + godot-theme, mask-tinted for both themes).
-->
<script setup lang="ts">
import { ref, computed, nextTick, onMounted, onUnmounted } from 'vue'
import { useData, withBase } from 'vitepress'

const { theme } = useData()
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

/* Node glyphs: neutral pictograms from public/icons/node, tinted like Godot's tree.
   Chrome glyphs come from the editor's own sets: godot-editor (editor icons) and
   godot-theme (GUI furniture) — always mask-tinted so light theme recolors correctly. */
const iconUrl = (name: string) => `url(${withBase(`/icons/node/${name}.svg`)})`
const edIconUrl = (name: string) => `url(${withBase(`/icons/godot-editor/${name}.svg`)})`
const thIconUrl = (name: string) => `url(${withBase(`/icons/godot-theme/${name}.svg`)})`

/* Godot's runbar, riding the menu strip's right edge. Play is lit: the scene — the dog — is running. */
const runbar = [
  { icon: 'hammer' },
  { icon: 'play', active: true },
  { icon: 'pause' },
  { icon: 'square' },
  { icon: 'clapperboard' },
  { icon: 'film_camera' },
  { icon: 'film' },
]

/* 2D viewport toolbar, exactly the editor's: select tool active, zoom at the right. */
const viewTools = ['ToolSelect', 'ToolMove', 'ToolRotate', 'ToolScale', 'ToolPan']
const viewLocks = ['Lock', 'Group', 'SnapGrid']

const hosts = [
  { name: 'MyGame.2dog', role: 'Desktop host', color: 'var(--ed-node-2d)', icon: 'window', link: '/project-layout' },
  { name: 'MyGame.web', role: 'Browser host', color: 'var(--ed-node-gui)', icon: 'globe', link: '/web' },
  { name: 'MyGame.tests', role: 'xUnit host', color: 'var(--ed-node-gold)', icon: 'test_tube', link: '/testing' },
]

/* FileSystem dock: the game's own files, visibly untouched. The hosts are absent on
   purpose — .gdignore keeps them out of the editor's sight, which IS the claim. */
const files = [
  { name: 'scenes', icon: 'Folder', folder: true },
  { name: 'scripts', icon: 'Folder', folder: true },
  { name: 'sprites', icon: 'Folder', folder: true },
  { name: 'icon.svg', icon: 'Image' },
  { name: 'main.tscn', icon: 'PackedScene' },
  { name: 'project.godot', icon: 'GodotMonochrome' },
]

/* Inspector dock: the four product claims as property categories of the running scene. */
const features = [
  {
    icon: 'globe',
    tint: '2d',
    name: 'C# on the Web',
    detail: 'Omg, C# Games in HTML5 and WASM! No more waiting for Godot.',
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

/* Signals tab: the project's real outward connections, in connection-dialog grammar.
   Every row is a live link; the pun rides on a real destination. */
const signals = [
  {
    sig: 'walkies_started()',
    conn: 'DogPark::join()',
    link: 'https://discord.gg/GAXdbZCNGT',
    label: 'Join the Dog Park — the 2dog channel on the outfox Discord',
  },
  {
    sig: 'stick_thrown()',
    conn: 'GitHub::fetch("outfox/2dog")',
    link: 'https://github.com/outfox/2dog',
    label: '2dog source on GitHub',
  },
  {
    sig: 'bug_sniffed(scent)',
    conn: 'GitHub::open_issue(scent)',
    link: 'https://github.com/outfox/2dog/issues',
    label: 'Report an issue on GitHub',
  },
  {
    sig: 'bone_buried(version)',
    conn: 'NuGet::install("2dog")',
    link: 'https://www.nuget.org/packages/2dog',
    label: 'The 2dog package on NuGet',
  },
]

/* Inspector | Signals: a real tab strip, like the dock's own. */
const inspectorTab = ref<'inspector' | 'signals'>('inspector')

function inspectorTabsKeydown(e: KeyboardEvent) {
  if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
  e.preventDefault()
  inspectorTab.value = inspectorTab.value === 'inspector' ? 'signals' : 'inspector'
  void nextTick(() =>
    document.getElementById(inspectorTab.value === 'inspector' ? 'itab-inspector' : 'itab-signals')?.focus()
  )
}

/* Filter Properties works for real: it narrows whichever list the active tab shows. */
const filterText = ref('')
const filterQuery = computed(() => filterText.value.trim().toLowerCase())
const filterPlaceholder = computed(() =>
  inspectorTab.value === 'inspector' ? 'Filter Properties' : 'Filter Signals'
)

const featMatch = (f: (typeof features)[number]) =>
  !filterQuery.value || `${f.name} ${f.detail} ${f.linkText}`.toLowerCase().includes(filterQuery.value)
const sigMatch = (s: (typeof signals)[number]) =>
  !filterQuery.value || `${s.sig} ${s.conn}`.toLowerCase().includes(filterQuery.value)

const visibleFeatures = computed(() => features.filter(featMatch))
const visibleSignals = computed(() => signals.filter(sigMatch))
const scriptRowVisible = computed(
  () => !filterQuery.value || 'script program.cs'.includes(filterQuery.value)
)

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
        <!-- Godot's runbar rides the menu strip; play is lit — the scene is running. -->
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

      <div class="ed-body">
        <!-- Left dock column: Scene above, FileSystem below, exactly like the shipping editor. -->
        <div class="ed-left">
          <nav class="ed-tree" aria-label="Scene: your project and its 2dog hosts">
            <!-- Scene is this page; Import is a real tab — it leads to the import docs. -->
            <div class="ed-dock-tabs">
              <span class="ed-dock-tab active" aria-hidden="true">Scene</span>
              <a class="ed-dock-tab" :href="withBase('/import-tool')">Import</a>
              <span class="strip-glyph" :style="{ '--gd-icon': thIconUrl('tabs_menu') }" aria-hidden="true"></span>
            </div>
            <div class="ed-dock-bar" aria-hidden="true">
              <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('Add') }"></span>
              <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('ToolConnect') }"></span>
              <span class="bar-well"><span
                class="bar-well-glyph"
                :style="{ '--gd-icon': thIconUrl('search') }"
              ></span>Filter Nodes</span>
            </div>
            <a class="ed-tree-root" :href="withBase('/project-layout')">
              <span class="tree-arrow" :style="{ '--gd-icon': edIconUrl('GuiTreeArrowDown') }" aria-hidden="true"></span>
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

          <!-- FileSystem shows the game exactly as the editor sees it: untouched. The hosts
               are missing here because .gdignore hides them — which is the whole promise. -->
          <section class="ed-files" aria-label="FileSystem: your game files, untouched by 2dog">
            <div class="ed-dock-tabs" aria-hidden="true">
              <span class="ed-dock-tab active">FileSystem</span>
              <span class="strip-glyph" :style="{ '--gd-icon': thIconUrl('tabs_menu') }"></span>
            </div>
            <div class="ed-file-row root">
              <span class="tree-arrow" :style="{ '--gd-icon': edIconUrl('GuiTreeArrowDown') }" aria-hidden="true"></span>
              <span class="file-glyph folder" :style="{ '--gd-icon': thIconUrl('folder') }" aria-hidden="true"></span>res://
            </div>
            <div v-for="f in files" :key="f.name" class="ed-file-row">
              <span
                class="file-glyph"
                :class="{ folder: f.folder }"
                :style="{ '--gd-icon': f.folder ? thIconUrl('folder') : edIconUrl(f.icon) }"
                aria-hidden="true"
              ></span>{{ f.name }}
            </div>
            <p class="ed-files-note">
              Your files, exactly as they were. The hosts sit beside them,
              <code>.gdignore</code>'d — the editor never even sees them.
            </p>
          </section>
        </div>

        <div class="ed-center">
          <!-- The 2D workspace toolbar: pure chrome, the select tool forever active. -->
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
              width="300"
              height="154"
            />
            <h1 class="ed-headline">Start, control, embed Godot in&nbsp;.NET</h1>
            <p class="ed-tagline">
              Keep your scenes, scripts, and editor workflow.<br />
              Gain an entire ecosystem.
            </p>
            <div class="ed-actions">
              <a class="ed-btn brand" :href="withBase('/getting-started')">Read the Dogs</a>
              <a class="ed-btn" href="https://github.com/outfox/2dog">Fetch on GitHub</a>
            </div>
          </div>
        </div>

        <aside class="ed-inspector" aria-label="Why 2dog">
          <!-- The dock's real anatomy: tab strip, resource toolbar, node selector, filter. -->
          <div class="ed-dock-tabs" role="tablist" aria-label="Inspector dock" @keydown="inspectorTabsKeydown">
            <button
              id="itab-inspector"
              class="ed-dock-tab"
              :class="{ active: inspectorTab === 'inspector' }"
              role="tab"
              aria-controls="ipanel-inspector"
              :aria-selected="inspectorTab === 'inspector'"
              :tabindex="inspectorTab === 'inspector' ? 0 : -1"
              @click="inspectorTab = 'inspector'"
            >Inspector</button>
            <button
              id="itab-signals"
              class="ed-dock-tab"
              :class="{ active: inspectorTab === 'signals' }"
              role="tab"
              aria-controls="ipanel-signals"
              :aria-selected="inspectorTab === 'signals'"
              :tabindex="inspectorTab === 'signals' ? 0 : -1"
              @click="inspectorTab = 'signals'"
            >Signals</button>
            <span class="strip-glyph" :style="{ '--gd-icon': thIconUrl('tabs_menu') }" aria-hidden="true"></span>
          </div>

          <div class="ed-dock-bar" aria-hidden="true">
            <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('New') }"></span>
            <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('Load') }"></span>
            <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('Save') }"></span>
            <span class="bar-spacer"></span>
            <span class="bar-glyph dim" :style="{ '--gd-icon': edIconUrl('Back') }"></span>
            <span class="bar-glyph dim" :style="{ '--gd-icon': edIconUrl('Forward') }"></span>
            <span class="bar-glyph" :style="{ '--gd-icon': edIconUrl('History') }"></span>
          </div>

          <div class="ed-insp-node" aria-hidden="true">
            <span class="node-glyph root" :style="{ '--gd-icon': iconUrl('joystick') }"></span>
            <span class="ed-insp-node-name">MyGame</span>
            <span class="node-drop" :style="{ '--gd-icon': thIconUrl('option_button_arrow') }"></span>
          </div>

          <div class="ed-insp-filter">
            <span class="filter-glyph" :style="{ '--gd-icon': thIconUrl('search') }" aria-hidden="true"></span>
            <input
              v-model="filterText"
              type="text"
              :placeholder="filterPlaceholder"
              :aria-label="filterPlaceholder"
            />
          </div>

          <div
            id="ipanel-inspector"
            v-show="inspectorTab === 'inspector'"
            class="ed-insp-panel"
            role="tabpanel"
            aria-labelledby="itab-inspector"
          >
            <div class="ed-props">
              <section v-for="f in visibleFeatures" :key="f.link" class="ed-prop">
                <h2 class="ed-cat">
                  <span
                    :class="['gd-icon', `gd-icon--${f.tint}`]"
                    :style="{ '--gd-icon': iconUrl(f.icon) }"
                    aria-hidden="true"
                  ></span>
                  {{ f.name }}
                </h2>
                <p class="ed-prop-detail">{{ f.detail }}</p>
                <a class="ed-section-link" :href="withBase(f.link)">
                  <span class="tree-arrow" :style="{ '--gd-icon': edIconUrl('GuiTreeArrowRight') }" aria-hidden="true"></span>
                  {{ f.linkText }}
                </a>
              </section>
            </div>
            <div v-show="scriptRowVisible" class="ed-script-row">
              <span class="ed-script-label">Script</span>
              <a
                class="ed-script-value"
                :href="withBase('/concepts')"
                aria-label="Script: Program.cs — how the .NET host drives Godot"
              >Program.cs</a>
            </div>
            <p v-if="visibleFeatures.length === 0 && !scriptRowVisible" class="ed-filter-empty">
              No property matches “{{ filterText.trim() }}”. 
            </p>
          </div>

          <div
            id="ipanel-signals"
            v-show="inspectorTab === 'signals'"
            class="ed-insp-panel"
            role="tabpanel"
            aria-labelledby="itab-signals"
          >
            <ul class="ed-signals">
              <li v-for="s in visibleSignals" :key="s.sig" class="ed-signal">
                <p class="ed-signal-name">
                  <span class="sig-glyph" :style="{ '--gd-icon': edIconUrl('MemberSignal') }" aria-hidden="true"></span>
                  {{ s.sig }}
                </p>
                <a class="ed-signal-conn" :href="s.link" :aria-label="s.label">
                  <span class="slot-glyph" :style="{ '--gd-icon': edIconUrl('Slot') }" aria-hidden="true"></span>
                  {{ s.conn }}
                </a>
              </li>
            </ul>
            <p v-if="visibleSignals.length === 0" class="ed-filter-empty">
              No signal matches “{{ filterText.trim() }}” — sniffed everywhere.
            </p>
          </div>
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

.host-controls {
  display: flex;
  gap: 10px;
  margin-left: auto;
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

.ed-body {
  display: grid;
  grid-template-columns: 236px 1fr 272px;
}

/* ---------- Dock anatomy shared by Scene, FileSystem, and Inspector ---------- */

/* Tab strips: the strip sits one step darker; the active tab is the panel color,
   connecting flush — Godot's own dock-tab construction. */
.ed-dock-tabs {
  display: flex;
  align-items: stretch;
  gap: 1px;
  padding: 3px 4px 0;
  background: var(--ed-dark-2);
}

.ed-dock-tab {
  padding: 5px 13px 6px;
  border: none;
  border-radius: 3px 3px 0 0;
  background: transparent;
  font-size: 12px;
  line-height: 1.3;
  color: var(--ed-text-2);
}

button.ed-dock-tab {
  cursor: pointer;
}

a.ed-dock-tab {
  text-decoration: none;
}

button.ed-dock-tab:hover,
a.ed-dock-tab:hover {
  color: var(--ed-text-1);
}

button.ed-dock-tab:focus-visible,
a.ed-dock-tab:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

.ed-dock-tab.active {
  background: var(--ed-dark-1);
  color: var(--ed-text-1);
}

.strip-glyph {
  align-self: center;
  margin-left: auto;
  margin-right: 4px;
  width: 13px;
  height: 13px;
  background-color: var(--ed-text-3);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

/* Dock toolbars: a quiet row of editor glyphs under the tabs. */
.ed-dock-bar {
  display: flex;
  align-items: center;
  gap: 11px;
  padding: 6px 10px;
  border-bottom: 1px solid var(--ed-seam);
}

.bar-glyph {
  flex: none;
  width: 14px;
  height: 14px;
  background-color: var(--ed-text-2);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

.bar-glyph.dim {
  background-color: var(--ed-text-3);
}

.bar-glyph.active {
  background-color: var(--ed-accent);
}

.bar-glyph.sm {
  width: 12px;
  height: 12px;
}

.bar-spacer {
  flex: 1;
}

.bar-sep {
  width: 1px;
  height: 14px;
  background: var(--ed-seam);
}

/* The Scene dock's filter box, rendered as chrome (the inspector's is the live one). */
.bar-well {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  padding: 3px 8px;
  border: 1px solid var(--ed-seam);
  border-radius: 3px;
  background: var(--ed-dark-2);
  font-size: 11.5px;
  color: var(--ed-text-3);
  white-space: nowrap;
  overflow: hidden;
}

.bar-well-glyph {
  flex: none;
  width: 12px;
  height: 12px;
  background-color: var(--ed-text-3);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

/* ---------- Left column: Scene + FileSystem docks ---------- */

.ed-left {
  display: flex;
  flex-direction: column;
  background: var(--ed-dark-1);
  border-right: 1px solid var(--ed-seam);
}

.ed-tree {
  padding-bottom: 10px;
}

.ed-tree-root,
.ed-tree-item {
  display: flex;
  align-items: baseline;
  gap: 7px;
  margin: 0 4px;
  padding: 5px 6px;
  border-radius: 3px;
  font-size: 13px;
  color: var(--ed-text-1);
  text-decoration: none;
}

.ed-tree-root {
  margin-top: 4px;
}

.ed-tree-item {
  margin-left: 22px;
  border-left: 1px solid var(--ed-seam);
  border-radius: 0 3px 3px 0;
}

.ed-tree-root:hover,
.ed-tree-item:hover {
  background: var(--ed-raise);
  color: var(--ed-accent);
}

.ed-tree-root:focus-visible,
.ed-tree-item:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

/* Godot's faint tree disclosure arrows (the icon carries its own 40% alpha). */
.tree-arrow {
  flex: none;
  width: 12px;
  height: 12px;
  align-self: center;
  background-color: var(--ed-text-1);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

.node-glyph {
  flex: none;
  width: 15px;
  height: 15px;
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

/* FileSystem dock: plain rows — this dock's job is to show nothing changed. */
.ed-files {
  flex: 1;
  display: flex;
  flex-direction: column;
  border-top: 1px solid var(--ed-seam);
}

.ed-file-row {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0 4px;
  padding: 3px 6px;
  font-family: var(--vp-font-family-mono);
  font-size: 12px;
  color: var(--ed-text-1);
}

.ed-file-row.root {
  margin-top: 6px;
}

.ed-file-row:not(.root) {
  margin-left: 22px;
  border-left: 1px solid var(--ed-seam);
}

.file-glyph {
  flex: none;
  width: 15px;
  height: 15px;
  background-color: var(--ed-text-2);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

/* Godot paints folders in the accent-tinted folder color. */
.file-glyph.folder {
  background-color: var(--ed-node-2d);
}

.ed-files-note {
  margin: auto 8px 10px;
  padding-top: 10px;
  font-size: 11px;
  line-height: 1.55;
  color: var(--ed-text-2);
}

.ed-files-note code {
  font-family: var(--vp-font-family-mono);
  font-size: 10.5px;
}

/* ---------- Center: 2D toolbar + viewport ---------- */

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

/* ---------- Inspector dock ---------- */

.ed-inspector {
  display: flex;
  flex-direction: column;
  background: var(--ed-dark-1);
  border-left: 1px solid var(--ed-seam);
  padding-bottom: 14px;
}

/* The node selector: which object these properties belong to. */
.ed-insp-node {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 7px 8px 0;
  padding: 4px 9px;
  border-radius: 3px;
  background: var(--ed-raise);
}

.ed-insp-node-name {
  font-family: var(--vp-font-family-mono);
  font-size: 12px;
  color: var(--ed-text-1);
}

.node-drop {
  margin-left: auto;
  width: 12px;
  height: 12px;
  background-color: var(--ed-text-2);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

/* Filter Properties: a real LineEdit doing real filtering. */
.ed-insp-filter {
  position: relative;
  margin: 6px 8px 0;
}

.filter-glyph {
  position: absolute;
  top: 50%;
  left: 8px;
  width: 13px;
  height: 13px;
  transform: translateY(-50%);
  background-color: var(--ed-text-3);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
  pointer-events: none;
}

.ed-insp-filter input {
  width: 100%;
  padding: 5px 9px 5px 27px;
  border: 1px solid var(--ed-seam);
  border-radius: 3px;
  background: var(--ed-dark-2);
  font-family: inherit;
  font-size: 12.5px;
  color: var(--ed-text-1);
}

.ed-insp-filter input::placeholder {
  color: var(--ed-text-3);
}

.ed-insp-filter input:focus {
  outline: 2px solid var(--ed-accent);
  outline-offset: -1px;
}

.ed-insp-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
}

/* Property categories: full-bleed centered strips, Godot's own category bars. */
.ed-cat {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  margin: 10px 0 0;
  padding: 5px 8px;
  background: var(--ed-raise);
  font-size: 12.5px;
  font-weight: 500;
  line-height: 1.4;
  color: var(--ed-text-1);
}

.ed-cat .gd-icon {
  flex: none;
  width: 14px;
  height: 14px;
}

.ed-prop-detail {
  margin: 7px 14px 2px;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--ed-text-2);
}

/* The claim's link, dressed as a property section row (chevron and all). */
.ed-section-link {
  display: flex;
  align-items: center;
  gap: 5px;
  margin: 1px 4px 0;
  padding: 4px 8px;
  border-radius: 3px;
  font-size: 12px;
  font-weight: 500;
  color: var(--ed-accent);
  text-decoration: none;
}

.ed-section-link:hover {
  background: var(--ed-raise);
  color: var(--ed-accent-strong);
}

.ed-section-link:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

/* Script: the property every Godot dev reads last — here it's your host program. */
.ed-script-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 14px 12px 0;
  font-size: 12.5px;
  color: var(--ed-text-2);
}

.ed-script-value {
  flex: 1;
  padding: 3px 9px;
  border: 1px solid var(--ed-seam);
  border-radius: 3px;
  background: var(--ed-dark-2);
  font-family: var(--vp-font-family-mono);
  font-size: 11.5px;
  color: var(--ed-accent);
  text-decoration: none;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ed-script-value:hover {
  border-color: var(--ed-accent);
  color: var(--ed-accent-strong);
}

.ed-script-value:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: 1px;
}

.ed-filter-empty {
  margin: 14px 14px 0;
  font-size: 12px;
  line-height: 1.5;
  color: var(--ed-text-2);
}

/* Signals: the project's outward connections, in the connections dialog's grammar. */
.ed-signals {
  list-style: none;
  margin: 8px 4px 0;
  padding: 0;
}

.ed-signal {
  margin-bottom: 7px;
}

.ed-signal-name {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0;
  padding: 3px 8px;
  font-family: var(--vp-font-family-mono);
  font-size: 12px;
  color: var(--ed-text-1);
}

.sig-glyph {
  flex: none;
  width: 13px;
  height: 13px;
  background-color: var(--ed-node-anim);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

.ed-signal-conn {
  display: flex;
  align-items: center;
  gap: 7px;
  margin-left: 22px;
  padding: 3px 9px;
  border-left: 1px solid var(--ed-seam);
  border-radius: 0 3px 3px 0;
  font-family: var(--vp-font-family-mono);
  font-size: 11.5px;
  color: var(--ed-accent);
  text-decoration: none;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ed-signal-conn:hover {
  background: var(--ed-raise);
  color: var(--ed-accent-strong);
}

.ed-signal-conn:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

.slot-glyph {
  flex: none;
  width: 13px;
  height: 13px;
  background-color: var(--ed-success);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
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

.ed-shell-wrap {
  position: relative;
  margin: 0;
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
    grid-template-columns: 236px 1fr;
  }

  .ed-inspector {
    grid-column: 1 / -1;
    border-left: none;
    border-top: 1px solid var(--ed-seam);
  }

  .ed-props {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    column-gap: 12px;
    align-items: start;
    padding: 0 8px;
  }

  .ed-cat {
    border-radius: 3px;
  }

  .ed-signals {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    column-gap: 12px;
  }
}

@media (max-width: 819px) {
  .editor-home {
    padding: 20px 16px 4px;
  }

  .ed-body {
    grid-template-columns: 1fr;
  }

  .ed-left {
    border-right: none;
    border-bottom: 1px solid var(--ed-seam);
  }

  .ed-tree {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 2px;
    padding: 0 4px 6px;
  }

  .ed-tree .ed-dock-tabs {
    flex-basis: 100%;
    margin: 0 -4px 4px;
  }

  .ed-tree .ed-dock-bar,
  .ed-files,
  .ed-view-bar {
    display: none;
  }

  .ed-tree-root {
    margin-top: 0;
  }

  .ed-tree-item {
    margin-left: 0;
    border-left: none;
    border-radius: 3px;
  }

  .tree-arrow {
    display: none;
  }

  .ed-tree-note {
    display: none;
  }

  .ed-props,
  .ed-signals {
    grid-template-columns: 1fr;
  }

  .host-runbar {
    display: none;
  }

  .ed-menubar {
    flex-wrap: wrap;
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
