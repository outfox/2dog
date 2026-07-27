<!-- Bottom panel, per the editor itself: Quickstart (the two-path choice, as docked
     subwindows) + Output (a real desktop-host run), tab strip at the bottom with the
     typing pun as a persistent status line and the version at the right edge. -->
<script setup lang="ts">
import { ref, computed, nextTick, onMounted, onUnmounted } from 'vue'
import { useData, withBase } from 'vitepress'
import { iconUrl } from './icons'
import { plain } from './content'

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
})

onUnmounted(() => {
  window.clearTimeout(typeTimer)
  window.clearInterval(rotateTimer)
  document.removeEventListener('visibilitychange', onVisibility)
})

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
      { comment: true, text: "# no install needed, just use .NET 10's dotnet tool execute (dnx)" },
      { comment: false, text: 'dnx 2dog add MyGame' },
      { comment: false, text: 'cd MyGame' },
      { comment: true, text: "# e.g. if you created a browser host in the tool, try:" },
      { comment: false, text: 'dotnet publish MyGame.web' },
    ],
    link: '/add',
    linkText: 'What 2dog adds →',
  },
  {
    icon: 'tennis_ball',
    tint: 'gui',
    title: "I'm Starting Fresh",
    code: [
      { comment: true, text: '# the 2dog tool will walk you through creation of a new project ' },
      { comment: false, text: 'dnx 2dog new MyGame' },
      { comment: false, text: 'cd MyGame' },
      { comment: true, text: "# there's also dotnet templates" },
      { comment: false, text: 'dotnet new install 2dog' },
    ],
    link: '/templates',
    linkText: 'Template options →',
  },
]
</script>

<template>
  <div class="ed-bottom">
    <!-- Tablist first in DOM for focus order; CSS `order` keeps it visually at the
         bottom, where Godot puts its bottom-panel tab strip. -->
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
      ></span> {{ punLine }}<span class="ed-caret" aria-hidden="true"></span></p>
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
              <span v-html="s.title"></span>
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
                  :aria-label="`Copy commands: ${plain(s.title)}`"
                  @click="copyCommands(s)"
                >{{ copied === s.link ? 'Copied!' : 'Copy' }}</button>
              </div>
              <a class="ed-subwin-link" :href="withBase(s.link)" v-html="s.linkText"></a>
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
</template>

<style>
/* Content wells above, tab strip at the bottom, everything flowing on one ground —
   no window chrome, no shadows, no seams inside the panel. */
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

/* minmax(0,…) so the track ignores the commands' min-content width: without it the
   panel grows to the longest line and the window's overflow:hidden clips it away. */
.ed-panels {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
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

/* Quickstart panel: the choice as two inset wells, Stack Frames / Breakpoints style. */
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
  min-width: 0;
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

/* `ed-` prefixed like every class in this directory: these styles are global, and
   a bare `.caret` would land on VitePress's own sidebar disclosure control. */
.ed-caret {
  display: inline-block;
  width: 7px;
  height: 12px;
  margin-left: 2px;
  vertical-align: -2px;
  background: var(--ed-accent);
  animation: ed-caret-blink 1.1s steps(1) infinite;
}

@keyframes ed-caret-blink {
  50% { opacity: 0; }
}

@media (prefers-reduced-motion: reduce) {
  .ed-caret {
    animation: none;
  }
}

@media (max-width: 819px) {
  /* Portrait: the two start paths stack. Side by side there is no column wide
     enough for a command line, so both would read through a scrollbar. */
  .ed-subwins {
    grid-template-columns: 1fr;
  }

  .ed-status-pun {
    display: none;
  }

  .ed-shell code {
    font-size: 11.5px;
  }
}
</style>
