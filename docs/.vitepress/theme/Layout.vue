<script setup lang="ts">
// theme-without-fonts, matching index.ts: importing 'vitepress/theme' here would
// drag VitePress's bundled Inter back in (~25 KB of @font-face in the critical
// CSS, 14 unused woff2 in the bundle) on top of our self-hosted copy.
import DefaultTheme from 'vitepress/theme-without-fonts'
import { useData } from 'vitepress'
import EditorHome from './EditorHome.vue'

const { Layout } = DefaultTheme
const { frontmatter } = useData()
</script>

<template>
  <Layout>
    <template #home-hero-before v-if="frontmatter.layout === 'home'">
      <EditorHome />
    </template>
  </Layout>
</template>
