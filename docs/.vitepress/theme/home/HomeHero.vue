<!-- Hero: the promise in words on the left, and on the right one game running in three hosts at once. -->
<script setup lang="ts">
import { ref, computed } from 'vue'
import { useData, withBase } from 'vitepress'
import { brIconUrl, iconUrl, webgpuLogoUrl } from './icons'

const { theme } = useData()
const twodogVersion = computed(() => (theme.value as any).twodogVersion ?? '')
const godotVersion = computed(() => (theme.value as any).godotVersion ?? '')

const commands = ['cd MyGame', 'dnx 2dog add --web', 'dotnet publish MyGame.web']
const copied = ref(false)

async function copyCommands() {
  try {
    await navigator.clipboard.writeText(commands.join('\n'))
    copied.value = true
    window.setTimeout(() => (copied.value = false), 2000)
  } catch {
    /* Clipboard unavailable (permissions or insecure context): the button keeps its label. */
  }
}
</script>

<template>
  <section class="hero" aria-labelledby="hero-title">
    <div class="hero-grid">
      <div class="hero-copy">
        <a class="hero-news" :href="withBase('/hosts/web')">
          <span class="hero-news-tag">New</span>
          <img class="hero-news-logo" :src="webgpuLogoUrl()" alt="" width="29" height="18" />
          WebGPU rendering for web builds
          <span aria-hidden="true">→</span>
        </a>

        <h1 id="hero-title" class="hero-title">Godot, where .NET&nbsp;goes.</h1>
        <p class="hero-lead">
          <strong>2dog</strong> embeds the Godot game engine in .NET <span class="gd-icon gd-icon--ok" :style="{ '--gd-icon': iconUrl('bone') }" aria-hidden="true"></span>
          <br/>Ship your C# game to the
          browser, embed it in desktop or mobile apps, and give it the <s>treats</s> tests it deserves.           
        </p>

        <div class="home-actions">
          <a class="home-btn primary" :href="withBase('/getting-started')">
           Read the Dogs!
          </a>
          <a class="home-btn" href="https://discord.gg/GAXdbZCNGT">
            <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('discord') }" aria-hidden="true"></span>
            Bark on Discord
          </a>
          <a class="home-btn" href="https://www.nuget.org/packages/2dog/">
            <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('nuget') }" aria-hidden="true"></span>
            Fetch on NuGet
          </a>
          <a class="home-btn" href="https://github.com/outfox/2dog">
            <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('github') }" aria-hidden="true"></span>
            Sniff the Source
          </a>
        </div>
        <ul class="hero-facts">
          <li>
            <span class="gd-icon gd-icon--ok" :style="{ '--gd-icon': iconUrl('heart') }" aria-hidden="true"></span>
            MIT licensed.
          </li>
          <li>2dog {{ twodogVersion }}</li>
          <li>Godot {{ godotVersion }}</li>
          <li>.NET 10</li>
        </ul>
      </div>

      <div class="hero-side">
        <!-- One labelled picture: the window chrome inside is drawing, not interface. -->
        <div
          class="hero-art"
          role="img"
          aria-label="The same 2dog game running at once in a desktop app window, a web browser and an Android phone"
        >
          <div class="art-win art-desktop">
            <div class="art-bar">
              <span class="art-title">MyGame.avalonia</span>
              <span class="art-controls"><i></i><i></i><i></i></span>
            </div>
            <div class="art-desktop-body">
              <div class="art-sidebar">
                <span class="art-line w80"></span>
                <span class="art-line w60"></span>
                <span class="art-toggle"></span>
                <span class="art-line w70"></span>
                <span class="art-slider"></span>
              </div>
              <div class="game-view art-view">
                <!-- The page's LCP element; without the hint the browser fetches it at low priority. -->
                <img
                  class="art-logo"
                  :src="withBase('/logo-animated.svg')"
                  alt=""
                  width="450"
                  height="231"
                  fetchpriority="high"
                />
              </div>
            </div>
          </div>

          <div class="art-win art-browser">
            <div class="art-bar">
              <span class="art-dots"><i></i><i></i><i></i></span>
              <span class="art-url">mygame.example</span>
            </div>
            <div class="game-view art-view">
              <img class="art-icon" :src="withBase('/icon.svg')" alt="" width="568" height="573" />
              <span class="art-badge">
                <img :src="webgpuLogoUrl()" alt="" width="19" height="12" />WebGPU
              </span>
            </div>
          </div>

          <div class="art-phone">
            <div class="game-view art-view">
              <img class="art-icon" :src="withBase('/icon.svg')" alt="" width="568" height="573" />
            </div>
          </div>
        </div>

        <div class="hero-term">
          <div class="hero-term-head">
            <span>From stock Godot to the browser</span>
            <button type="button" class="hero-copy-btn" @click="copyCommands">
              {{ copied ? 'Copied' : 'Copy' }}
            </button>
          </div>
          <pre tabindex="0" aria-label="Shell commands"><code><template v-for="c in commands" :key="c"><span class="prompt" aria-hidden="true">$ </span>{{ c }}
</template></code></pre>
          <span class="visually-hidden" aria-live="polite">{{ copied ? 'Commands copied' : '' }}</span>
        </div>
      </div>
    </div>
  </section>
</template>

<style>
.hero {
  padding: 56px 24px 96px;
  background:
    linear-gradient(var(--ed-grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--ed-grid) 1px, transparent 1px),
    var(--ed-base);
  background-size: 32px 32px, 32px 32px, auto;
}

.hero-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.05fr);
  gap: 56px;
  align-items: center;
  max-width: 1152px;
  margin: 0 auto;
}

.hero-news {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 28px;
  padding: 6px 14px 6px 6px;
  border: 1px solid var(--ed-seam);
  border-radius: 999px;
  background: var(--ed-raise);
  color: var(--ed-text-1);
  font-size: 15px;
  font-weight: 500;
  text-decoration: none;
}

.hero-news:hover {
  border-color: var(--ed-accent);
}

.hero-news-tag {
  padding: 2px 10px;
  border-radius: 999px;
  background: var(--ed-accent);
  color: var(--ed-on-accent);
  font-size: 13px;
  font-weight: 700;
}

.hero-news-logo {
  width: 29px;
  height: 18px;
}

.hero-title {
  margin: 0;
  font-size: clamp(42px, 6vw, 68px);
  line-height: 1.04;
  font-weight: 800;
  letter-spacing: -0.035em;
  color: var(--ed-text-1);
}

.hero-lead {
  max-width: 34em;
  margin: 24px 0 32px;
  font-size: 19px;
  line-height: 1.6;
  color: var(--ed-text-2);
}

.hero-lead strong {
  color: var(--ed-text-1);
}

.hero-facts {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 20px;
  margin: 28px 0 0;
  padding: 0;
  list-style: none;
  font-size: 15px;
  color: var(--ed-text-2);
}

.hero-facts li {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.hero-facts li:first-child {
  color: var(--ed-text-1);
  font-weight: 600;
}

/* ---------- The picture: three hosts, one game ---------- */

.hero-art {
  position: relative;
  aspect-ratio: 10 / 7.4;
}

.art-win,
.art-phone {
  position: absolute;
  overflow: hidden;
  border: 1px solid var(--ed-seam);
  background: var(--ed-raise);
  box-shadow: 0 18px 40px -18px rgba(0, 0, 0, 0.5);
}

.art-win {
  display: flex;
  flex-direction: column;
  border-radius: 10px;
}

.art-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 30px;
  padding: 0 12px;
  border-bottom: 1px solid var(--ed-seam);
  background: var(--ed-dark-1);
  font-size: 12px;
  color: var(--ed-text-2);
}

.art-title {
  font-weight: 600;
}

.art-controls {
  display: flex;
  gap: 10px;
  margin-left: auto;
}

.art-controls i {
  width: 9px;
  height: 9px;
  border: 1.5px solid var(--ed-text-3);
  border-radius: 2px;
}

.art-dots {
  display: flex;
  gap: 6px;
}

.art-dots i {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--ed-text-3);
  opacity: 0.6;
}

.art-url {
  flex: 1;
  padding: 2px 10px;
  border-radius: 999px;
  background: var(--ed-base);
  font-family: var(--vp-font-family-mono);
  font-size: 11px;
}

.art-view {
  flex: 1;
  min-height: 0;
  position: relative;
}

.art-desktop {
  top: 0;
  left: 0;
  width: 84%;
  height: 70%;
}

.art-desktop-body {
  flex: 1;
  display: flex;
  min-height: 0;
}

/* The Avalonia app's own controls beside the game. */
.art-sidebar {
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 26%;
  padding: 16px 14px;
  border-right: 1px solid var(--ed-seam);
}

.art-line {
  height: 8px;
  border-radius: 4px;
  background: var(--ed-text-3);
  opacity: 0.35;
}

.art-line.w80 { width: 80%; }
.art-line.w70 { width: 70%; }
.art-line.w60 { width: 60%; }

.art-toggle {
  width: 30px;
  height: 16px;
  border-radius: 999px;
  background: var(--chip-avalonia);
}

.art-slider {
  height: 4px;
  border-radius: 2px;
  background: linear-gradient(90deg, var(--chip-avalonia) 60%, var(--ed-seam) 60%);
}

.art-logo {
  width: 78%;
  height: auto;
}

.art-browser {
  left: 6%;
  bottom: 0;
  width: 56%;
  height: 46%;
}

.art-browser .art-icon {
  width: 30%;
  height: auto;
}

.art-badge {
  position: absolute;
  right: 8px;
  bottom: 8px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 3px 9px;
  border-radius: 999px;
  background: #fff;
  color: #005a9c;
  font-size: 11px;
  font-weight: 700;
}

.art-badge img {
  width: 19px;
  height: 12px;
}

.art-phone {
  right: 0;
  bottom: 4%;
  width: 22%;
  aspect-ratio: 9 / 18;
  padding: 7px;
  border-radius: 20px;
  background: var(--ed-dark-2);
}

.art-phone .art-view {
  height: 100%;
  border-radius: 13px;
}

.art-phone .art-icon {
  width: 70%;
  height: auto;
}

/* ---------- Terminal ---------- */

.hero-term {
  margin-top: 28px;
  border: 1px solid var(--ed-seam);
  border-radius: 10px;
  background: var(--ed-dark-2);
  overflow: hidden;
}

.hero-term-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 6px 6px 6px 16px;
  border-bottom: 1px solid var(--ed-seam);
  font-size: 14px;
  color: var(--ed-text-2);
}

.hero-copy-btn {
  min-width: 72px;
  min-height: 36px;
  padding: 0 14px;
  border: 1px solid var(--ed-seam);
  border-radius: 6px;
  background: var(--ed-raise);
  color: var(--ed-text-1);
  font: inherit;
  font-weight: 600;
  cursor: pointer;
}

.hero-copy-btn:hover {
  border-color: var(--ed-accent);
}

.hero-term pre {
  margin: 0;
  padding: 14px 16px 16px;
  overflow-x: auto;
  font-family: var(--vp-font-family-mono);
  font-size: 15px;
  line-height: 1.75;
  color: var(--ed-text-1);
}

.hero-term .prompt {
  color: var(--ed-node-gui);
  user-select: none;
}

:root:not(.dark) .hero-term .prompt {
  color: var(--ed-success);
}

@media (max-width: 959px) {
  .hero-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 48px;
  }

  .hero-side {
    max-width: 640px;
  }
}

@media (max-width: 719px) {
  .hero {
    padding: 32px 16px 64px;
  }

  .hero-lead {
    font-size: 17px;
  }

  .hero-news {
    font-size: 14px;
  }

  .art-bar {
    height: 22px;
    font-size: 10px;
  }

  .art-url {
    font-size: 9px;
  }

  .art-sidebar {
    gap: 8px;
    padding: 10px 8px;
  }

  .art-badge {
    font-size: 9px;
  }

  .hero-term pre {
    font-size: 14px;
  }
}
</style>
