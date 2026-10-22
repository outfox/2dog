<!-- Every host 2dog sets up. Each group wears one palette hue on its label, chips and hover ring; a chip
     shows the product's own mark, or a monogram where none exists. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { brIconUrl, iconUrl } from './icons'

// `size` is the mark's optical box in px (default 28), so heavy squares and thin wordmarks read alike.
type Host = {
  name: string
  flag: string
  text: string
  link: string
  brand?: string
  mono?: string
  size?: number
  tag?: string
}

// `soon` closes the group with a placeholder card for hosts that are planned but not yet set up.
const groups: { label: string; icon: string; hue: string; hosts: Host[]; soon?: boolean }[] = [
  {
    label: 'Web',
    hue: 'var(--hue-teal)',
    icon: 'globe',
    hosts: [
      {
        name: 'WebAssembly',
        flag: '--web',
        text: 'C# on the web, with WebGL or WebGPU.',
        link: '/hosts/web',
        brand: 'webassembly',
        size: 24,
      },
      {
        name: 'WebXR',
        flag: '--webxr',
        text: 'VR and AR sessions, right in the browser or on device.',
        link: '/hosts/webxr',
        brand: 'webxr',
        size: 30,
      },
    ],
  },
  {
    label: 'Testing',
    hue: 'var(--hue-amber)',
    icon: 'test_tube',
    hosts: [
      {
        name: 'GdUnit',
        flag: '--gdunit',
        text: 'Run your GdUnit suites with <code>dotnet test</code>.',
        link: '/hosts/gdunit',
        mono: 'Gd',
      },
      {
        name: 'NUnit',
        flag: '--nunit',
        text: 'Fixtures, assertions, expectations &amp; more.',
        link: '/hosts/nunit',
        brand: 'nunit',
      },
      {
        name: 'xUnit',
        flag: '--xunit',
        text: 'Frame waits, assertions, parallel collections &amp; more.',
        link: '/hosts/xunit',
        brand: 'xunit',
        size: 26,
      },
    ],
  },
  {
    label: 'Desktop',
    hue: 'var(--hue-lime)',
    icon: 'desktop',
    hosts: [
      {
        name: 'Avalonia',
        flag: '--avalonia',
        text: 'An Avalonia control, zero-copy on the GPU.',
        link: '/hosts/avalonia',
        brand: 'avaloniaui',
      },
      {
        name: 'Qt Quick',
        flag: '--qt',
        text: 'A <code>GodotView</code> in QML with Qt Quick on top.',
        link: '/hosts/qt',
        brand: 'qt',
        tag: 'Experimental',
      },
      {
        name: 'WinForms',
        flag: '--winforms',
        text: 'The engine in a Windows Forms window.',
        link: '/hosts/winforms',
        brand: 'dotnet',
        size: 32,
      },
      {
        name: 'WinUI 3',
        flag: '--winui',
        text: 'Godot in a Windows App SDK window.',
        link: '/hosts/winui',
        brand: 'winui',
        size: 30,
      },
    ],
  },
  {
    label: 'Fullstack',
    hue: 'var(--hue-violet)',
    icon: 'server',
    hosts: [
      {
        name: 'Blazor',
        flag: '--blazor',
        text: 'A <code>GodotView</code> component for Blazor.',
        link: '/hosts/blazor',
        brand: 'blazor',
      },
    ],
    soon: true,
  },
  {
    label: 'Mobile',
    hue: 'var(--hue-rose)',
    icon: 'mobile_phone',
    hosts: [
      {
        name: 'Android',
        flag: '--android',
        text: 'Package your game as an Android APK.',
        link: '/hosts/android',
        brand: 'android',
        size: 30,
        tag: 'Experimental',
      },
      {
        name: 'iOS',
        flag: '--ios',
        text: 'Publish to iPhone, iPad, and Simulators (Apple silicon).',
        link: '/hosts/ios',
        mono: 'iOS',
        tag: 'Experimental',
      },
      {
        name: 'MAUI',
        flag: '--maui',
        text: 'A .NET MAUI page on Windows, Android and iOS.',
        link: '/hosts/maui',
        brand: 'maui',
        size: 32,
      },
    ],
  },
  {
    label: 'Consoles',
    hue: 'var(--hue-slate)',
    icon: 'joypad',
    hosts: [],
    soon: true,
  },
  {
    label: 'Tooling',
    hue: 'var(--hue-coral)',
    icon: 'code',
    hosts: [
      {
        name: '2dog',
        flag: '--generic',
        text: 'Your custom <code>Main()</code> drives every frame.',
        link: '/hosts/generic',
        brand: '2dog',
        size: 34,
      },
      {
        name: 'REPL',
        flag: '--repl',
        text: 'Explore and edit a game while it runs.',
        link: '/hosts/repl',
        mono: '>_',
        tag: 'Experimental',
      },
    ],
  },
]

const glyphStyle = (h: Host) => ({ '--gd-icon': brIconUrl(h.brand!), '--glyph': h.size ? `${h.size}px` : undefined })
</script>

<template>
  <section class="home-section alt" aria-labelledby="hosts-title">
    <div class="home-wrap">
      <div class="hosts-head">
        <div>
          <p class="home-kicker">
            <span class="gd-icon" :style="{ '--gd-icon': iconUrl('container') }" aria-hidden="true"></span>Hosts
          </p>
          <h2 id="hosts-title" class="home-h2">Every Dog has its Day</h2>
          <p class="home-intro">
            A host is a small .NET project that starts Godot and runs your game. Add as many as you like
            with <code>dnx 2dog add</code>. They're just tiny sidecar projects!
          </p>
        </div>
        <a class="hosts-more" :href="withBase('/hosts/')">What's a host? <span aria-hidden="true">→</span></a>
      </div>

      <div v-for="g in groups" :key="g.label" class="host-group" :style="{ '--mark': g.hue }">
        <h3 class="host-group-label">
          <span
            class="gd-icon"
            :style="{ '--gd-icon': iconUrl(g.icon) }"
            aria-hidden="true"
          ></span>{{ g.label }}
        </h3>
        <ul class="host-list">
          <li v-for="h in g.hosts" :key="h.name">
            <a class="host-card" :href="withBase(h.link)">
              <span class="brand-chip" aria-hidden="true">
                <span v-if="h.mono" class="chip-mono" :class="{ long: h.mono.length > 2 }">{{ h.mono }}</span>
                <span v-else class="chip-glyph" :style="glyphStyle(h)"></span>
              </span>
              <span class="host-body">
                <span class="host-name">
                  {{ h.name }}<span v-if="h.tag" class="host-tag">{{ h.tag }}</span>
                </span>
                <span class="host-text" v-html="h.text"></span>
                <code class="host-flag">{{ h.flag }}</code>
              </span>
            </a>
          </li>
          <li v-if="g.soon">
            <div class="host-card soon">
              <span class="brand-chip soon" aria-hidden="true">
                <span class="chip-glyph" :style="{ '--gd-icon': iconUrl('ellipsis_horizontal'), '--glyph': '16px' }"></span>
              </span>
              <span class="host-body">
                <span class="host-name">
                  Coming Soon
                </span>
                <span class="host-text"><i>... doggedly developing ...</i></span>
              </span>
            </div>
          </li>
        </ul>
      </div>
    </div>
  </section>
</template>

<style>
.hosts-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 24px;
  margin-bottom: 48px;
}

.hosts-more {
  flex: none;
  font-weight: 600;
  color: var(--ed-accent);
  text-decoration: none;
}

.hosts-more:hover {
  text-decoration: underline;
}

.host-group {
  display: grid;
  grid-template-columns: 168px minmax(0, 1fr);
  gap: 24px;
  padding: 24px 0;
  border-top: 1px solid var(--ed-seam);
}

.host-group-label {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 12px 0 0;
  align-self: start;
  font-size: 18px;
  font-weight: 700;
  color: var(--ed-text-1);
}

.host-group-label .gd-icon {
  width: 16px;
  height: 16px;
  background-color: var(--mark);
}

.host-list {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.host-card {
  display: flex;
  gap: 16px;
  height: 100%;
  padding: 18px;
  border: 1px solid var(--ed-seam);
  border-radius: 10px;
  background: var(--ed-base);
  color: var(--ed-text-1);
  text-decoration: none;
  transition: border-color 0.12s ease-out, transform 0.12s ease-out;
}

.host-card:hover {
  border-color: color-mix(in srgb, var(--mark) 70%, var(--ed-seam));
  transform: translateY(-2px);
}

.host-card:not(.soon):hover .brand-chip,
.host-card:focus-visible .brand-chip {
  background: var(--mark);
  color: var(--chip-ink);
  box-shadow: inset 0 0 0 1px var(--mark);
}

/* Placeholder: a neutral slate outline, not a link, so it neither lifts nor highlights on hover. Its minimum
   height matches a host card with two lines of text, so a lone placeholder (Consoles) lines up too. */
.host-card.soon,
.host-card.soon:hover {
  align-items: center;
  min-height: 139px;
  border: 1px dashed color-mix(in srgb, var(--hue-slate) 55%, var(--ed-seam));
  background: transparent;
  transform: none;
}

.host-card.soon .host-text {
  color: var(--ed-text-3);
}

.host-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.host-name {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  font-size: 17px;
  font-weight: 700;
}

.host-tag {
  padding: 1px 8px;
  border: 1px solid var(--ed-seam);
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
  color: var(--ed-text-2);
}

.host-text {
  font-size: 15px;
  line-height: 1.5;
  color: var(--ed-text-2);
}

.host-card .host-flag {
  align-self: flex-start;
  margin-top: auto;
  padding: 0;
  background: none;
  font-size: 13px;
  color: var(--ed-accent);
}

@media (max-width: 1023px) {
  .host-group {
    grid-template-columns: minmax(0, 1fr);
    gap: 16px;
  }

  .host-group-label {
    margin: 0;
  }

  .host-list {
    grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  }
}

@media (max-width: 719px) {
  .hosts-head {
    flex-direction: column;
    align-items: flex-start;
    margin-bottom: 32px;
  }

  .host-list {
    grid-template-columns: minmax(0, 1fr);
    gap: 12px;
  }

  .host-card {
    padding: 16px;
  }
}
</style>
