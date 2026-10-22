<!-- Every host 2dog scaffolds. Each group wears one palette hue on its label and chips, whether the
     chip shows a brand mark or a Godot pictogram. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { brIconUrl, iconUrl } from './icons'

type Host = {
  name: string
  flag: string
  text: string
  link: string
  brand?: string
  glyph?: string
  tag?: string
}

// `soon` closes the group with a placeholder card for hosts that are planned but not yet scaffolded.
const groups: { label: string; icon: string; hue: string; hosts: Host[]; soon?: boolean }[] = [
  {
    label: 'Web',
    hue: 'var(--hue-sky)',
    icon: 'globe',
    hosts: [
      {
        name: 'WebAssembly',
        flag: '--web',
        text: 'C# Games as an AppBundle. WebGL, and soon WebGPU.<br/>No more waiting for Godot!',
        link: '/hosts/web',
        brand: 'webassembly',
      },
      {
        name: 'WebXR',
        flag: '--webxr',
        text: 'Godot VR and AR sessions, right in the browser.',
        link: '/hosts/webxr',
        glyph: 'vr_headset',
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
        text: 'Run your GDScript GdUnit suites with <code>dotnet test</code>.',
        link: '/hosts/gdunit',
        glyph: 'test_tube',
      },
      {
        name: 'NUnit',
        flag: '--nunit',
        text: 'Fixtures and signal assertions on a real engine.',
        link: '/hosts/nunit',
        glyph: 'test_tube',
      },
      {
        name: 'xUnit',
        flag: '--xunit',
        text: 'Collections, frame waits and assertions for xUnit v3.',
        link: '/hosts/xunit',
        glyph: 'test_tube',
      },
    ],
  },
  {
    label: 'Desktop',
    hue: 'var(--hue-coral)',
    icon: 'desktop',
    hosts: [
      {
        name: 'Avalonia',
        flag: '--avalonia',
        text: 'Godot as a control in cross-platform Avalonia apps - in a zero-copy GPU surface.',
        link: '/hosts/avalonia',
        brand: 'avaloniaui',
      },
      {
        name: 'Qt Quick',
        flag: '--qt',
        text: 'A <code>GodotView</code> in your QML scene, with Qt Quick items over the game.',
        link: '/hosts/qt',
        brand: 'qt',
        tag: 'Experimental',
      },
      {
        name: 'WinForms',
        flag: '--winforms',
        text: 'Drop the engine into a Windows Forms window.',
        link: '/hosts/winforms',
        glyph: 'window',
      },
      {
        name: 'WinUI 3',
        flag: '--winui',
        text: 'Host Godot in a modern Windows App SDK window.',
        link: '/hosts/winui',
        glyph: 'windows',
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
        text: 'A <code>GodotView</code> component in your Blazor WebAssembly, served by ASP.NET Core.',
        link: '/hosts/blazor',
        brand: 'blazor',
      },
    ],
    soon: true,
  },
  {
    label: 'Mobile',
    hue: 'var(--hue-teal)',
    icon: 'mobile_phone',
    hosts: [
      {
        name: 'Android',
        flag: '--android',
        text: 'Package your game as an Android APK.',
        link: '/hosts/android',
        brand: 'android',
        tag: 'Experimental',
      },
      {
        name: 'iOS',
        flag: '--ios',
        text: 'Build an iPhone or iPad app, or try it in the Simulator on an Apple silicon Mac.',
        link: '/hosts/ios',
        glyph: 'mobile_phone',
        tag: 'Experimental',
      },
      {
        name: 'MAUI',
        flag: '--maui',
        text: 'Embed Godot in a .NET MAUI page on Windows, Android and iOS.',
        link: '/hosts/maui',
        glyph: 'mobile_phone',
      },
    ],
  },
  {
    label: 'Consoles',
    hue: 'var(--hue-amber)',
    icon: 'joypad',
    hosts: [],
    soon: true,
  },
  {
    label: 'Tooling',
    hue: 'var(--hue-lime)',
    icon: 'code',
    hosts: [
      {
        name: '2dog',
        flag: '--generic',
        text: 'Your own <code>Main()</code>: start Godot, run code before, during, or after each frame.',
        link: '/hosts/generic',
        brand: 'dotnet',
      },
      {
        name: 'REPL',
        flag: '--repl',
        text: 'Explore, edit, play, or inspect the game from a C# prompt while it is running.',
        link: '/hosts/repl',
        glyph: 'window_terminal',
        tag: 'Experimental',
      },
    ],
  },
]

const glyphUrl = (h: Host) => (h.brand ? brIconUrl(h.brand) : iconUrl(h.glyph!))
</script>

<template>
  <section class="home-section alt" aria-labelledby="hosts-title">
    <div class="home-wrap">
      <div class="hosts-head">
        <div>
          <p class="home-kicker">
            <span class="gd-icon" :style="{ '--gd-icon': iconUrl('container') }" aria-hidden="true"></span>Hosts
          </p>
          <h2 id="hosts-title" class="home-h2">Dog of all Trades</h2>
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
                <span class="chip-glyph" :style="{ '--gd-icon': glyphUrl(h) }"></span>
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
              <span class="brand-chip" aria-hidden="true">
                <span class="chip-glyph" :style="{ '--gd-icon': iconUrl('ellipsis_horizontal') }"></span>
              </span>
              <span class="host-body">
                <span class="host-text">More coming soon.</span>
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
  border-color: var(--ed-accent);
  transform: translateY(-2px);
}

/* Placeholder: an outline in the group's hue, not a link, so it neither lifts nor highlights on hover. */
.host-card.soon,
.host-card.soon:hover {
  align-items: center;
  border: 1px dashed color-mix(in srgb, var(--mark) 45%, var(--ed-seam));
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
