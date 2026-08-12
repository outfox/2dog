<!-- Inspector dock: the four product claims as property categories of the running
     scene, with the dock's real anatomy — tab strip (Inspector | Signals are real
     tabs), resource toolbar, node selector, and a working property filter. -->
<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { withBase } from 'vitepress'
import { iconUrl, edIconUrl, thIconUrl } from './icons'
import { plain } from './content'

/* name/detail/linkText accept inline HTML — see content.ts for the rule. */
const features = [
  {
    icon: 'globe',
    tint: '2d',
    name: 'Godot C# on the Web',
    detail: 'Finally... no more waiting for Godot.',
    link: '/getting-started.html#_7-publish-to-the-browser',
    linkText: 'Quickstart',
  },
  {
    icon: 'joystick',
    tint: '3d',
    name: '2dog is still Godot',
    detail: "It's a companion, not a replacement.",
    link: '/project-layout',
    linkText: 'Godot or good dog?',
  },
  {
    icon: 'test_tube',
    tint: 'gui',
    name: 'Automation-Test like a Pro',
    detail: "Test scenes, scripts, and resources.",
    link: '/testing',
    linkText: "Read more",
  },
  {
    icon: 'curly_brackets',
    tint: 'gold',
    name: '.NET has the Leash',
    detail: 'Your process controls Godot.',
    link: '/concepts',
    linkText: 'Concepts',
  },
]

/* Signals tab: the project's real outward connections. Grouped under class
   categories the way the dock groups by declaring class, each signal disclosing
   its connection. Every connection row is a live link; the pun rides on a real
   destination. `sig`/`conn` accept inline HTML — see content.ts. */
const signalGroups = [
  {
    klass: 'MyGame',
    icon: iconUrl('joystick'),
    tint: 'var(--ed-accent)',
    signals: [
      {
        sig: 'walkies_started()',
        conn: 'DogPark :: join()',
        link: 'https://discord.gg/GAXdbZCNGT',
        label: 'Join the Dog Park — the 2dog channel on the outfox Discord',
      },
      {
        sig: 'stick_thrown()',
        conn: 'GitHub :: fetch("outfox/2dog")',
        link: 'https://github.com/outfox/2dog',
        label: '2dog source on GitHub',
      },
    ],
  },
  {
    klass: 'Node',
    icon: edIconUrl('Node'),
    tint: 'var(--ed-node-gui)',
    signals: [
      {
        sig: 'bug_sniffed(scent)',
        conn: 'GitHub :: open_issue(scent)',
        link: 'https://github.com/outfox/2dog/issues',
        label: 'Report an issue on GitHub',
      },
      {
        sig: 'bone_buried(version)',
        conn: 'NuGet :: install("2dog")',
        link: 'https://www.nuget.org/packages/2dog',
        label: 'The 2dog package on NuGet',
      },
    ],
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

/* Match the words the reader sees, never the markup carrying them. */
const featMatch = (f: (typeof features)[number]) =>
  !filterQuery.value ||
  plain(`${f.name} ${f.detail} ${f.linkText}`).toLowerCase().includes(filterQuery.value)
type Signal = (typeof signalGroups)[number]['signals'][number]
const sigMatch = (s: Signal) =>
  !filterQuery.value || plain(`${s.sig} ${s.conn}`).toLowerCase().includes(filterQuery.value)

const visibleFeatures = computed(() => features.filter(featMatch))

/* Naming a class keeps its whole group, as filtering a tree by section would. */
const visibleSignalGroups = computed(() =>
  signalGroups
    .map((g) => ({
      ...g,
      signals: g.klass.toLowerCase().includes(filterQuery.value) ? g.signals : g.signals.filter(sigMatch),
    }))
    .filter((g) => g.signals.length > 0)
)
const signalHits = computed(() =>
  visibleSignalGroups.value.reduce((n, g) => n + g.signals.length, 0)
)
const scriptRowVisible = computed(
  () => !filterQuery.value || 'script program.cs'.includes(filterQuery.value)
)
</script>

<template>
  <aside class="ed-inspector" aria-label="Why 2dog">
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

    <!-- The node selector: which object these properties belong to. -->
    <div class="ed-insp-node" aria-hidden="true">
      <span class="node-glyph root" :style="{ '--gd-icon': iconUrl('joystick') }"></span>
      <span class="ed-insp-node-name">MyGame</span>
      <span class="node-drop" :style="{ '--gd-icon': thIconUrl('option_button_arrow') }"></span>
    </div>

    <div class="ed-dock-filter ed-insp-filter">
      <span class="ed-dock-filter-glyph" :style="{ '--gd-icon': thIconUrl('search') }" aria-hidden="true"></span>
      <input
        v-model="filterText"
        type="text"
        :placeholder="filterPlaceholder"
        :aria-label="filterPlaceholder"
      />
    </div>

    <!-- All panels share one grid cell; the ghost reserves the unfiltered height,
         so tab switches and filtering never resize the host window. -->
    <div class="ed-insp-panels">
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
              <span v-html="f.name"></span>
            </h2>
            <p class="ed-prop-detail" v-html="f.detail"></p>
            <a class="ed-section-link" :href="withBase(f.link)">
              <span class="tree-arrow" :style="{ '--gd-icon': edIconUrl('GuiTreeArrowRight') }" aria-hidden="true"></span>
              <span v-html="f.linkText"></span>
            </a>
          </section>
        </div>
        <!-- Script: the property every Godot dev reads last — here it's your host program. -->
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
        <div class="ed-sig-tree">
          <section v-for="g in visibleSignalGroups" :key="g.klass">
            <h3 class="ed-sig-cat">
              <span
                class="tree-arrow"
                :style="{ '--gd-icon': edIconUrl('GuiTreeArrowDown') }"
                aria-hidden="true"
              ></span>
              <span
                class="sig-class-glyph"
                :style="{ '--gd-icon': g.icon, backgroundColor: g.tint }"
                aria-hidden="true"
              ></span>
              {{ g.klass }}
            </h3>
            <div v-for="s in g.signals" :key="s.sig">
              <p class="ed-sig-name">
                <span
                  class="tree-arrow"
                  :style="{ '--gd-icon': edIconUrl('GuiTreeArrowDown') }"
                  aria-hidden="true"
                ></span>
                <span class="sig-glyph" :style="{ '--gd-icon': edIconUrl('Signal') }" aria-hidden="true"></span>
                <span v-html="s.sig"></span>
              </p>
              <a class="ed-sig-conn" :href="s.link" :aria-label="plain(s.label)">
                <span class="slot-glyph" :style="{ '--gd-icon': edIconUrl('Slot') }" aria-hidden="true"></span>
                <span v-html="s.conn"></span>
              </a>
            </div>
          </section>
        </div>
        <p v-if="signalHits === 0" class="ed-filter-empty">
          No signal matches “{{ filterText.trim() }}” — sniffed everywhere.
        </p>
      </div>

      <!-- Height ghosts: the full unfiltered contents of both tabs, invisible, sharing
           the cell so it reserves whichever is taller. Spans instead of headings and
           links — they must never be reachable. -->
      <div class="ed-insp-panel ghost" aria-hidden="true">
        <div class="ed-sig-tree">
          <section v-for="g in signalGroups" :key="g.klass">
            <div class="ed-sig-cat">
              <span class="tree-arrow"></span>
              <span class="sig-class-glyph"></span>
              {{ g.klass }}
            </div>
            <div v-for="s in g.signals" :key="s.sig">
              <p class="ed-sig-name">
                <span class="tree-arrow"></span>
                <span class="sig-glyph"></span>
                <span v-html="s.sig"></span>
              </p>
              <span class="ed-sig-conn">
                <span class="slot-glyph"></span>
                <span v-html="s.conn"></span>
              </span>
            </div>
          </section>
        </div>
      </div>

      <div class="ed-insp-panel ghost" aria-hidden="true">
        <div class="ed-props">
          <section v-for="f in features" :key="f.link" class="ed-prop">
            <div class="ed-cat">
              <span class="gd-icon" :style="{ '--gd-icon': iconUrl(f.icon) }"></span>
              <span v-html="f.name"></span>
            </div>
            <p class="ed-prop-detail" v-html="f.detail"></p>
            <span class="ed-section-link">
              <span class="tree-arrow"></span>
              <span v-html="f.linkText"></span>
            </span>
          </section>
        </div>
        <div class="ed-script-row">
          <span class="ed-script-label">Script</span>
          <span class="ed-script-value">Program.cs</span>
        </div>
      </div>
    </div>
  </aside>
</template>

<style>
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
  font-size: 12.5px;
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

/* Filter Properties: the shared dock LineEdit (dock.css), inset from the panel edge. */
.ed-insp-filter {
  flex: none;
  margin: 6px 8px 0;
}

/* One grid cell for all panels: the invisible ghost reserves the unfiltered height,
   so switching tabs or filtering never changes the host window's size. */
.ed-insp-panels {
  flex: 1;
  display: grid;
}

.ed-insp-panels > * {
  grid-area: 1 / 1;
}

.ed-insp-panel {
  display: flex;
  flex-direction: column;
}

.ed-insp-panel.ghost {
  visibility: hidden;
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

a.ed-section-link:hover {
  background: var(--ed-raise);
  color: var(--ed-accent-strong);
}

a.ed-section-link:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: -2px;
}

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

a.ed-script-value:hover {
  border-color: var(--ed-accent);
  color: var(--ed-accent-strong);
}

a.ed-script-value:focus-visible {
  outline: 2px solid var(--ed-accent);
  outline-offset: 1px;
}

.ed-filter-empty {
  margin: 14px 14px 0;
  font-size: 12px;
  line-height: 1.5;
  color: var(--ed-text-2);
}

/* Signals dock: a Tree, not the inspector's property list. Class categories carry a
   full-width bar and a disclosure arrow; each signal discloses its connection one
   level deeper. Three indent steps, 16px apart, like the editor's own tree. */
.ed-sig-tree {
  margin-top: 6px;
}

/* Left-aligned, unlike the inspector's centred category strips — the dock's
   sections are tree rows, and Godot draws them that way. */
.ed-sig-cat {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 5px 0 1px;
  padding: 4px 8px;
  background: var(--ed-raise);
  font-size: 12.5px;
  font-weight: 500;
  line-height: 1.4;
  color: var(--ed-text-1);
}

.sig-class-glyph {
  flex: none;
  width: 14px;
  height: 14px;
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

.ed-sig-name {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 0;
  padding: 2px 8px 2px 14px;
  font-family: var(--vp-font-family-mono);
  font-size: 12px;
  color: var(--ed-text-1);
}

/* Signal.svg ships #ff5f5f; the categorical red carries it into both themes. */
.sig-glyph {
  flex: none;
  width: 13px;
  height: 13px;
  background-color: var(--ed-node-3d);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

/* The connection, one step under its signal — blue because here it is a real link. */
.ed-sig-conn {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 0 4px 2px 30px;
  padding: 2px 6px;
  border-radius: 3px;
  font-family: var(--vp-font-family-mono);
  font-size: 11.5px;
  color: var(--ed-accent);
  text-decoration: none;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* The label is its own span (v-html), so it — not the flex row — must truncate. */
.ed-sig-conn > span:last-child {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ed-sig-conn:hover {
  background: var(--ed-raise);
  color: var(--ed-accent-strong);
}

.ed-sig-conn:focus-visible {
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

/* Narrow: the inspector redocks below the viewport, as Godot does when docks collapse. */
@media (max-width: 1139px) {
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

  .ed-sig-tree {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    column-gap: 12px;
    align-items: start;
    padding: 0 8px;
  }
}

@media (max-width: 819px) {
  .ed-props,
  .ed-sig-tree {
    grid-template-columns: 1fr;
  }
}
</style>
