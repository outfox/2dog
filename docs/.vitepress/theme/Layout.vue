<script setup lang="ts">
import DefaultTheme from 'vitepress/theme'
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
