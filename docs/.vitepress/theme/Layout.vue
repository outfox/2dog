<script setup lang="ts">
import DefaultTheme from 'vitepress/theme'
import { useData } from 'vitepress'
import HeroCarousel from './HeroCarousel.vue'

const { Layout } = DefaultTheme
const { frontmatter } = useData()
</script>

<template>
  <Layout>
    <template #home-hero-info v-if="frontmatter.layout === 'home'">
      <HeroCarousel />
    </template>
  </Layout>
</template>
