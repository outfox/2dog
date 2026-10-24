<!-- One small schematic per New Trick, drawn 1:1 on a 240x120 canvas. Colors follow the page's names: Godot
     sky, .NET violet, and 2dog's lime for the path the trick adds; amber marks a game's scene and the rest stays
     neutral. -->
<script lang="ts">
export type Diagram = 'webgpu' | 'instances' | 'double' | 'wasm-threads' | 'zero-copy' | 'single-file' | 'readback'

// The parts a single-file publish folds in, in their owners' colors.
const singleFiles = [
  { label: 'libgodot', tone: 'c-godot' },
  { label: 'Game.pck', tone: 'c-godot' },
  { label: 'Game.dll', tone: 'c-net' },
  { label: 'ext.dll', tone: 'c-mute' },
]
</script>

<script setup lang="ts">
defineProps<{ name: Diagram }>()
</script>

<template>
  <svg class="trick-svg" viewBox="0 0 240 120" width="240" height="120" aria-hidden="true">
    <!-- Godot renders through WebGPU (the logo's triangle strip, shaded like it) to a desktop window and a
         browser tab. -->
    <template v-if="name === 'webgpu'">
      <g class="c-godot">
        <rect class="f" x="8" y="40" width="56" height="40" rx="6" />
        <text x="36" y="64" text-anchor="middle">Godot</text>
      </g>
      <g class="c-hot">
        <path class="s flow" d="M66 60 H92" />
        <path class="s" d="M88 56 l4 4 -4 4" />
        <path class="shade" style="--k: 45%" d="M90 41 H133.3 L111.7 78.5 Z" />
        <path class="shade" style="--k: 60%" d="M133.3 41 L144.2 59.8 H122.5 Z" />
        <path class="shade" style="--k: 72%" d="M122.5 59.8 H144.2 L133.3 78.5 Z" />
        <path class="shade" style="--k: 86%" d="M138.8 50.4 H149.6 L144.2 59.8 Z" />
        <path class="shade" style="--k: 100%" d="M144.2 41 L149.6 50.4 H138.8 Z" />
        <text x="120" y="98" text-anchor="middle">WebGPU</text>
        <path class="s flow" d="M146 60 C160 60 160 36 176 36 M146 60 C160 60 160 86 176 86" />
        <path class="s" d="M172 32 l4 4 -4 4 M172 82 l4 4 -4 4" />
      </g>
      <g class="c-mute">
        <rect class="f" x="182" y="18" width="48" height="32" rx="3" />
        <path class="s" d="M206 50 V57 M196 58 H216" />
        <rect class="f" x="182" y="70" width="48" height="34" rx="3" />
        <path class="s" d="M182 78 H230" />
        <circle class="d" cx="187" cy="74" r="1.2" /><circle class="d" cx="191" cy="74" r="1.2" />
      </g>
      <g class="c-hot">
        <path class="d" d="M206 26 L214 41 H198 Z M206 82 L214 97 H198 Z" />
      </g>
    </template>

    <!-- Three isolated engines, each with its own scene, inside one .NET process. -->
    <template v-else-if="name === 'instances'">
      <g class="c-net">
        <rect class="f" x="8" y="8" width="224" height="104" rx="8" />
        <text x="18" y="26">.NET process</text>
      </g>
      <g v-for="(x, i) in [18, 88, 158]" :key="x" class="c-godot" :style="{ '--delay': `${i * 0.15}s` }">
        <rect class="f" :x="x" y="36" width="64" height="66" rx="6" />
        <text :x="x + 32" y="53" text-anchor="middle">Godot</text>
        <g class="c-scene">
          <!-- A whole number of 8-unit dashes, so the flowing outline has no seam. -->
          <rect class="f flow" :x="x + 8" y="60" width="48" height="34" rx="3" pathLength="160" />
          <circle v-if="i === 0" class="d flip" :cx="x + 32" cy="77" r="9" />
          <path v-else-if="i === 1" class="d spin tri" :d="`M${x + 32} 64.14 l12 20.78 h-24 Z`" />
          <rect v-else class="d spin" :x="x + 23" y="68" width="18" height="18" rx="2" />
        </g>
      </g>
    </template>

    <!-- How far from the origin the world stays precise: a small ring for float, most of the map for double. -->
    <template v-else-if="name === 'double'">
      <g class="c-hot grow">
        <path class="z" d="M24 104 V8 H194.9 A196 196 0 0 1 220 104 Z" />
        <path class="s flow" d="M194.9 8 A196 196 0 0 1 220 104" />
      </g>
      <g class="c-hot">
        <text x="194" y="24" text-anchor="end">double</text>
      </g>
      <g class="c-warn">
        <path class="z" d="M24 104 V56 A48 48 0 0 1 72 104 Z" />
        <path class="s" d="M24 56 A48 48 0 0 1 72 104" />
        <text x="28" y="80">float</text>
      </g>
      <g class="c-mute">
        <path class="s" d="M24 104 H232 M24 104 V8" />
        <path class="s dash grow" d="M24 104 L142 64" />
        <circle class="d" cx="24" cy="104" r="3" />
      </g>
      <g class="c-godot grow">
        <circle class="f" cx="150" cy="60" r="8" />
        <ellipse class="s" cx="150" cy="60" rx="14" ry="4" transform="rotate(-20 150 60)" />
      </g>
    </template>

    <!-- A browser tab with .NET, Godot and audio on their own threads, busy at the same time. -->
    <template v-else-if="name === 'wasm-threads'">
      <g class="c-mute">
        <rect class="f" x="8" y="8" width="224" height="104" rx="6" />
        <path class="s" d="M8 24 H232" />
        <circle class="d" cx="17" cy="16" r="2" /><circle class="d" cx="24" cy="16" r="2" />
        <circle class="d" cx="31" cy="16" r="2" />
        <rect class="s" x="44" y="12" width="120" height="8" rx="4" />
        <path class="s fine" d="M66 40 H222 M66 58 H222 M66 76 H222 M66 94 H222" />
        <text x="18" y="44">main</text>
        <rect class="d" x="68" y="36" width="14" height="8" rx="2" />
        <rect class="d" x="140" y="36" width="10" height="8" rx="2" />
      </g>
      <g class="c-net">
        <text x="18" y="62">.NET</text>
        <rect class="f" x="70" y="54" width="54" height="8" rx="2" />
        <rect class="f" x="132" y="54" width="36" height="8" rx="2" />
        <rect class="f" x="176" y="54" width="44" height="8" rx="2" />
      </g>
      <g class="c-godot">
        <text x="18" y="80">Godot</text>
        <rect class="f" x="80" y="72" width="44" height="8" rx="2" />
        <rect class="f" x="130" y="72" width="62" height="8" rx="2" />
        <rect class="f" x="198" y="72" width="22" height="8" rx="2" />
      </g>
      <g class="c-hot">
        <text x="18" y="98">audio</text>
        <path
          class="s flow"
          d="M68 94 q6 -6 12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0 t12 0"
        />
      </g>
    </template>

    <!-- Godot and the UI share one GPU texture, so a frame never leaves the GPU. -->
    <template v-else-if="name === 'zero-copy'">
      <g class="c-godot">
        <rect class="f" x="8" y="40" width="52" height="40" rx="6" />
        <text x="34" y="64" text-anchor="middle">Godot</text>
      </g>
      <g class="c-mute">
        <rect class="s dash" x="74" y="22" width="84" height="74" rx="6" />
        <text x="80" y="35">GPU</text>
      </g>
      <g class="c-hot">
        <rect class="f" x="94" y="44" width="44" height="32" rx="2" />
        <path class="s fine" d="M105 44 V76 M116 44 V76 M127 44 V76 M94 55 H138 M94 66 H138" />
        <path class="s flow" d="M62 60 H90" />
        <path class="s" d="M86 56 l4 4 -4 4" />
        <path class="s flow" d="M140 60 H168" />
        <path class="s" d="M164 56 l4 4 -4 4" />
        <text x="116" y="112" text-anchor="middle">DMA</text>
      </g>
      <g class="c-mute">
        <rect class="f" x="172" y="18" width="60" height="84" rx="4" />
        <path class="s" d="M172 28 H232 M178 38 H190 M178 46 H188 M178 54 H190" />
      </g>
      <g class="c-hot">
        <rect class="f" x="194" y="34" width="32" height="62" rx="2" />
      </g>
    </template>

    <!-- The fallback: each frame leaves the GPU, passes through the CPU and lands in the UI as pixels. -->
    <template v-else-if="name === 'readback'">
      <g class="c-godot">
        <rect class="f" x="8" y="22" width="52" height="36" rx="6" />
        <text x="34" y="44" text-anchor="middle">Godot</text>
      </g>
      <g class="c-mute">
        <rect class="s dash" x="74" y="8" width="84" height="60" rx="6" />
        <text x="80" y="21">GPU</text>
        <path class="s" d="M104 82 V77 M112 82 V77 M120 82 V77 M128 82 V77 M104 106 V111 M112 106 V111 M120 106 V111 M128 106 V111" />
        <rect class="f" x="95" y="82" width="42" height="24" rx="4" />
        <text x="116" y="98" text-anchor="middle">CPU</text>
      </g>
      <g class="c-hot">
        <rect class="f" x="96" y="28" width="40" height="28" rx="2" />
        <path class="s fine" d="M106 28 V56 M116 28 V56 M126 28 V56 M96 37 H136 M96 47 H136" />
        <path class="s flow" d="M62 40 H92" />
        <path class="s" d="M88 36 l4 4 -4 4" />
        <path class="s flow" d="M116 58 V74" />
        <path class="s" d="M112 70 l4 4 4 -4" />
        <path class="s flow" d="M140 94 C156 94 152 64 168 64" />
        <path class="s" d="M164 60 l4 4 -4 4" />
      </g>
      <g class="c-mute">
        <rect class="f" x="172" y="18" width="60" height="84" rx="4" />
        <path class="s" d="M172 28 H232 M178 38 H190 M178 46 H188 M178 54 H190" />
      </g>
      <g class="c-hot">
        <rect class="f" x="194" y="34" width="32" height="62" rx="2" />
        <path class="s fine" d="M194 46 H226 M194 58 H226 M194 70 H226 M194 82 H226 M204 34 V96 M215 34 V96" />
      </g>
    </template>

    <!-- The engine, the game's pack, its assembly and a GDExtension fold into one executable. -->
    <template v-else-if="name === 'single-file'">
      <g v-for="(f, i) in singleFiles" :key="f.label" :class="f.tone">
        <path class="f" :d="`M8 ${12 + i * 26} h72 l8 8 v14 h-80 Z`" />
        <text x="14" :y="27 + i * 26">{{ f.label }}</text>
      </g>
      <g class="c-hot">
        <path
          class="s flow"
          d="M90 23 C116 23 120 60 144 60 M90 49 C112 49 120 60 144 60 M90 75 C112 75 120 60 144 60 M90 101 C116 101 120 60 144 60"
        />
        <path class="s" d="M140 56 l4 4 -4 4" />
        <path class="f" d="M154 14 h54 l16 16 v76 h-70 Z" />
        <path class="s" d="M208 14 v16 h16" />
        <text x="189" y="46" text-anchor="middle">Game.exe</text>
      </g>
      <g v-for="(f, i) in singleFiles" :key="f.label" :class="f.tone">
        <rect class="d" x="164" :y="56 + i * 11" width="50" height="7" rx="2" />
      </g>
    </template>
  </svg>
</template>

<style>
.trick-svg {
  display: block;
  flex: none;
  color: var(--ed-text-3);
}

.trick-svg .c-godot { color: var(--id-godot); }
.trick-svg .c-net { color: var(--id-dotnet); }
.trick-svg .c-hot { color: var(--ed-accent); }
.trick-svg .c-warn { color: var(--ed-warning); }
.trick-svg .c-mute { color: var(--ed-text-3); }
/* A game's own scene, warm against the cool Godot and .NET hues. */
.trick-svg .c-scene { color: var(--hue-amber); }

/* f: tinted shape, z: tinted area without outline, s: line, d: solid mark. The f tint stays opaque, so lines
   behind a shape do not show through. */
.trick-svg .f {
  fill: color-mix(in srgb, currentColor 16%, var(--trick-panel));
  stroke: currentColor;
  stroke-width: 1.5;
}

.trick-svg .z {
  fill: color-mix(in srgb, currentColor 10%, transparent);
}

.trick-svg .s {
  fill: none;
  stroke: currentColor;
  stroke-width: 1.5;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.trick-svg .s.fine {
  stroke-width: 1;
  opacity: 0.55;
}

.trick-svg .s.dash {
  stroke-width: 1;
  stroke-dasharray: 3 3;
}

.trick-svg .d {
  fill: currentColor;
}

/* An opaque step (--k) between the panel and the hue; the matching stroke closes seams between neighbors. */
.trick-svg .shade {
  fill: color-mix(in srgb, currentColor var(--k), var(--trick-panel));
  stroke: color-mix(in srgb, currentColor var(--k), var(--trick-panel));
  stroke-width: 0.5;
  stroke-linejoin: round;
}

/* Shapes that turn on hover pivot on their own centers, the equilateral triangle on its centroid, so each
   turn of --turn lands the shape on itself. */
.trick-svg .spin,
.trick-svg .flip {
  transform-box: fill-box;
  transform-origin: center;
}

.trick-svg .spin {
  --turn: 90deg;
}

/* The double-precision world, its distance line and planet grow out from the origin at (24, 104). */
.trick-svg .grow {
  transform-box: view-box;
  transform-origin: 24px 104px;
}

.trick-svg .spin.tri {
  --turn: 120deg;
  transform-origin: 50% 66.67%;
}

.trick-svg text {
  font-family: var(--vp-font-family-mono), monospace;
  font-size: 11px;
  fill: currentColor;
}
</style>
