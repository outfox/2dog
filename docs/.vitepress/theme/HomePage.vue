<!--
The homepage, read top to bottom: the promise and one game in three hosts; every host with its brand;
WebGPU on the web; three claims shown in real code; free and libre open source. Godot's editor palette,
grid and pictograms carry the identity, while text stays at reading sizes and every control is a real link.
-->
<script setup lang="ts">
import HomeHero from './home/HomeHero.vue'
import HostShowcase from './home/HostShowcase.vue'
import WebGpuBand from './home/WebGpuBand.vue'
import WhyTwodog from './home/WhyTwodog.vue'
import FreeBand from './home/FreeBand.vue'
import './home/home.css'
</script>

<template>
  <!-- <main>: the home layout renders no other main landmark (VPDoc's is doc-pages only). -->
  <main class="home-page">
    <HomeHero />
    <HostShowcase />
    <WebGpuBand />
    <WhyTwodog />
    <FreeBand />
  </main>
</template>
