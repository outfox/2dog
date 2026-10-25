<!--
The homepage, read top to bottom: the promise and one game in three hosts; every host with its brand;
new engine capabilities; two claims shown in real code; free and libre open source. Godot's editor palette,
grid and pictograms carry the identity, while text stays at reading sizes and every control is a real link.
-->
<script setup lang="ts">
import HomeHero from './home/HomeHero.vue'
import HostShowcase from './home/HostShowcase.vue'
import NewTricksBand from './home/NewTricksBand.vue'
import LoveDogsAndRobotsBand from './home/LoveDogsAndRobotsBand.vue'
import FreeBand from './home/FreeBand.vue'
import './home/home.css'
</script>

<template>
  <!-- <main>: the home layout renders no other main landmark (VPDoc's is doc-pages only). -->
  <main class="home-page">
    <HomeHero />
    <HostShowcase />
    <NewTricksBand />
    <LoveDogsAndRobotsBand />
    <FreeBand />
  </main>
</template>
