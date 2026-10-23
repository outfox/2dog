<!-- Hero: the promise in words on the left, and on the right one game running in three hosts at once. -->
<script setup lang="ts">
import { ref, computed } from 'vue'
import { useData, withBase } from 'vitepress'
import { brIconUrl, iconUrl } from './icons'
import type { ThemeConfig } from '../config'

const { theme } = useData<ThemeConfig>()
const twodogVersion = computed(() => theme.value.twodogVersion ?? '')
const godotVersion = computed(() => theme.value.godotVersion ?? '')

const commands = ['dnx 2dog add --web', 'dotnet publish MyGame.web']
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
        <!--
        <a class="hero-news" :href="withBase('/hosts/web')">
          <span class="hero-news-tag">New</span>
          <img class="hero-news-logo" :src="webgpuLogoUrl()" alt="" width="29" height="18" />
          WebGPU rendering for web builds
          <span aria-hidden="true">→</span>
        </a>
        -->
        <h1 id="hero-title" class="hero-title">Godot, where .NET&nbsp;goes.  <span class="gd-icon hero-axes" :style="{ '--gd-icon': iconUrl('arrow_local_axes') }" aria-hidden="true"></span></h1>
        <p class="hero-lead">
          <strong>2dog</strong> <span class="lead-dog" aria-hidden="true"><span class="gd-icon lead-icon" :style="{ '--gd-icon': iconUrl('bone') }"></span></span> runs the <strong>Godot</strong> <span class="gd-icon lead-icon godot" :style="{ '--gd-icon': iconUrl('gobot') }" aria-hidden="true"></span> engine in <strong>.NET</strong> <span class="gd-icon lead-icon dotnet" :style="{ '--gd-icon': iconUrl('box') }" aria-hidden="true"></span>
          <br/>Ship your C# game to the
          browser, wrap it in desktop or mobile UI, and give it the <s>treats</s> tests it deserves.           
        </p>

        <div class="home-actions">
          <a class="home-btn primary" :href="withBase('/getting-started')">
           Read the Dogs!
          </a>
          <a class="home-btn" href="https://www.nuget.org/packages/2dog/">
            <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('nuget') }" aria-hidden="true"></span>
            Fetch from NuGet
          </a>
          <a class="home-btn" href="https://github.com/outfox/2dog">
            <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('github') }" aria-hidden="true"></span>
            Sniff the Source
          </a>
          <a class="home-btn" href="https://discord.gg/GAXdbZCNGT">
            <span class="brand-glyph" :style="{ '--gd-icon': brIconUrl('discord') }" aria-hidden="true"></span>
            Bark on Discord
          </a>
        </div>
        <ul class="hero-facts">
          <li>2dog {{ twodogVersion }}</li>
          <li>Godot {{ godotVersion }}</li>
          <li>.NET 10</li>
          <li>
            <span class="gd-icon hero-heart" :style="{ '--gd-icon': iconUrl('heart') }" aria-hidden="true"></span>
            MIT licensed.
          </li>
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
              <span class="art-url">https://mygame.example</span>
            </div>
            <div class="game-view art-view">
              <img class="art-icon" :src="withBase('/icon.svg')" alt="" width="568" height="573" />
            <!---
              <span class="art-badge">
                <img :src="webgpuLogoUrl()" alt="" width="19" height="12" />WebGPU
              </span>
            -->
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
          <pre tabindex="0" aria-label="Shell commands"><code><template v-for="c in commands" :key="c"><span class="prompt" aria-hidden="true">~/MyGame&gt; </span>{{ c }}
</template></code></pre>
          <span class="visually-hidden" aria-live="polite">{{ copied ? 'Commands copied' : '' }}</span>
        </div>
      </div>
    </div>
  </section>
</template>

<style src="./HomeHero.css"></style>
