// Version markers for the docs.
//
// The single source of truth for all versions is the repo-root
// Directory.Build.props. This module parses it at docs build time and
// provides markdown-it plugins that replace the markers
//
//   :2dog-version:     -> e.g. 4.7.0.24  (GodotVersion.TwoDogRevision)
//   :godot-version:    -> e.g. 4.7.0     (GodotVersion)
//   :natives-version:  -> e.g. 4.7.0     (GodotVersion.NativesRevision,
//                                         NuGet-normalized: a trailing .0
//                                         revision is dropped)
//
// everywhere in the markdown, including inside fenced code blocks and
// inline code.

import { readFileSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import type MarkdownIt from 'markdown-it'

const propsPath = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../Directory.Build.props'
)

function msbuildProperty(xml: string, name: string): string {
  const match = xml.match(new RegExp(`<${name}>([^<]+)</${name}>`))
  if (!match) {
    throw new Error(`Property <${name}> not found in ${propsPath}`)
  }
  return match[1].trim()
}

const props = readFileSync(propsPath, 'utf-8')

export const godotVersion = msbuildProperty(props, 'GodotVersion')
const twodogRevision = msbuildProperty(props, 'TwoDogRevision')
const nativesRevision = msbuildProperty(props, 'NativesRevision')

export const twodogVersion = `${godotVersion}.${twodogRevision}`
// NuGet normalizes a trailing zero revision away (4.7.0.0 publishes as 4.7.0).
export const nativesVersion =
  nativesRevision === '0' ? godotVersion : `${godotVersion}.${nativesRevision}`

function versionMarkerPlugin(marker: string, value: string) {
  const pattern = new RegExp(
    marker.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'),
    'g'
  )
  return (md: MarkdownIt) => {
    md.core.ruler.push(`version-marker-${marker}`, (state) => {
      for (const token of state.tokens) {
        if (token.content.includes(marker)) {
          token.content = token.content.replace(pattern, value)
        }
        for (const child of token.children ?? []) {
          if (child.content.includes(marker)) {
            child.content = child.content.replace(pattern, value)
          }
        }
      }
    })
  }
}

export const twodogVersionPlugin = versionMarkerPlugin(':2dog-version:', twodogVersion)
export const godotVersionPlugin = versionMarkerPlugin(':godot-version:', godotVersion)
export const nativesVersionPlugin = versionMarkerPlugin(':natives-version:', nativesVersion)
