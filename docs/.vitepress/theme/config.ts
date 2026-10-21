import type { DefaultTheme } from 'vitepress'

export interface ThemeConfig extends DefaultTheme.Config {
  twodogVersion: string
  godotVersion: string
}
