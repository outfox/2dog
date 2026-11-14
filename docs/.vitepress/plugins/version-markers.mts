// Version markers for the docs.
//
// All versions come from the Directory.Build.props of the newest stable release (stable-props.mjs), so the docs
// describe the release plain `dnx 2dog` installs. This module parses it at docs build time and
// provides markdown-it plugins that replace the markers
//
//   :2dog-version:     -> e.g. 4.7.0.24  (GodotVersion.TwoDogRevision[-TwoDogPrerelease])
//   :godot-version:    -> e.g. 4.7.0     (GodotVersion)
//   :natives-version:  -> e.g. 4.7.0     (GodotVersion.NativesRevision,
//                                         NuGet-normalized: a trailing .0
//                                         revision is dropped)
//
// everywhere in the markdown, including inside fenced code blocks and
// inline code.

import type MarkdownIt from 'markdown-it'
import { stableProps } from '../../stable-props.mjs'

function msbuildProperty(xml: string, name: string): string {
  const match = xml.match(new RegExp(`<${name}>([^<]*)</${name}>`))
  if (!match) {
    throw new Error(`Property <${name}> not found in the stable release's Directory.Build.props`)
  }
  return match[1].trim()
}

const props = stableProps()

export const godotVersion = msbuildProperty(props, 'GodotVersion')
const twodogRevision = msbuildProperty(props, 'TwoDogRevision')
const nativesRevision = msbuildProperty(props, 'NativesRevision')

const prerelease = msbuildProperty(props, 'TwoDogPrerelease')

// 2dog versions use Godot's numbers only (GodotVersion 4.8.0-beta -> 4.8.0.152-beta).
const godotRelease = godotVersion.split('-')[0]
export const twodogVersion = `${godotRelease}.${twodogRevision}${prerelease ? `-${prerelease}` : ''}`
// NuGet normalizes a trailing zero revision away (4.7.0.0 publishes as 4.7.0).
export const nativesVersion =
  nativesRevision === '0' ? godotRelease : `${godotRelease}.${nativesRevision}`

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
