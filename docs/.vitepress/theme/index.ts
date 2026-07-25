// theme-without-fonts: Inter is self-hosted from public/fonts (vendored from the
// Godot fork, OFL) instead of VitePress's bundled copy — see custom.css.
import DefaultTheme from 'vitepress/theme-without-fonts'
import Layout from './Layout.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  Layout
}
