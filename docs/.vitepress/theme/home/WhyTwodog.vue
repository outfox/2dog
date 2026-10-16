<!-- Three claims, each shown rather than told: the host loop and the xUnit test are trimmed from the
     template's own Program.cs and BasicTests.cs; the tree is the layout `2dog add` produces. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { iconUrl, edIconUrl } from './icons'

const tree = [
  { name: 'MyGame/', icon: 'Folder', folder: true, depth: 0 },
  { name: 'project.godot', icon: 'GodotMonochrome', depth: 1 },
  { name: 'main.tscn', icon: 'PackedScene', depth: 1 },
  { name: 'scripts/', icon: 'Folder', folder: true, depth: 1 },
  { name: 'MyGame.web/', icon: 'Folder', folder: true, depth: 1, host: true },
  { name: 'MyGame.xunit/', icon: 'Folder', folder: true, depth: 1, host: true },
]
</script>

<template>
  <section class="home-section" aria-labelledby="why-title">
    <div class="home-wrap">
      <p class="home-kicker">
        <span class="gd-icon" :style="{ '--gd-icon': iconUrl('paw_print') }" aria-hidden="true"></span>Why 2dog
      </p>
      <h2 id="why-title" class="home-h2">Made with love, dogs + robots.</h2>

      <div class="why-grid">
        <article class="why-card">
          <h3>.NET holds the leash</h3>
          <p>
            Start Godot from your own <code>Main()</code>, run each frame when you choose, and reach every
            node from C#.
          </p>
          <pre class="why-code" tabindex="0" aria-label="C# host loop"><code><span class="k">using var</span> engine = <span class="k">new</span> <span class="t">Engine</span>(<span class="s">"MyGame"</span>, args: args);
engine.<span class="f">Start</span>();

<span class="c">// Iteration() returns true when Godot quits.</span>
<span class="k">while</span> (!engine.<span class="f">Iteration</span>())
{
    <span class="c">// Your per-frame logic here</span>
}</code></pre>
          <a class="why-link" :href="withBase('/concepts')">Core concepts <span aria-hidden="true">→</span></a>
        </article>

        <article class="why-card">
          <h3>Test it like any .NET code</h3>
          <p>
            Load scenes, await signals and assert on real engine state with xUnit, NUnit or GdUnit, in your
            IDE and in CI.
          </p>
          <pre class="why-code" tabindex="0" aria-label="xUnit test"><code>[<span class="t">Collection</span>&lt;<span class="t">HeadlessCollection</span>&gt;]
<span class="k">public class</span> <span class="t">SceneTests</span>(<span class="t">HeadlessFixture</span> godot)
{
    [<span class="t">Fact</span>]
    <span class="k">public void</span> <span class="f">MainSceneLoads</span>()
    {
        <span class="k">var</span> main = <span class="t">GD</span>.<span class="f">Load</span>&lt;<span class="t">PackedScene</span>&gt;(<span class="s">"res://main.tscn"</span>)
            .<span class="f">Instantiate</span>();
        godot.Tree.Root.<span class="f">AddChild</span>(main);
        <span class="t">Assert</span>.<span class="f">True</span>(main.<span class="f">IsInsideTree</span>());
        main.<span class="f">Free</span>();
    }
}</code></pre>
          <a class="why-link" :href="withBase('/testing/')">Testing guide <span aria-hidden="true">→</span></a>
        </article>

        <article class="why-card wide">
          <div class="why-wide-text">
            <h3>2dog is still Godot, just backwards</h3>
            <p>
              Your scenes, scripts and editor stay exactly as they are. Hosts live beside them in folders
              that Godot ignores, so the editor and your teammates recognize the same project as before.
            </p>
            <a class="why-link" :href="withBase('/project-layout')">Project layout <span aria-hidden="true">→</span></a>
          </div>
          <ul class="why-tree" aria-label="Project layout after adding two hosts">
            <li v-for="e in tree" :key="e.name" :class="{ host: e.host }" :style="{ '--depth': e.depth }">
              <span
                class="why-tree-glyph"
                :class="{ folder: e.folder }"
                :style="{ '--gd-icon': edIconUrl(e.icon) }"
                aria-hidden="true"
              ></span>
              <span>{{ e.name }}</span>
              <span v-if="e.host" class="why-tree-note">.gdignore</span>
            </li>
          </ul>
        </article>
      </div>
    </div>
  </section>
</template>

<style>
.home-page {
  --code-bg: #f7f7f7;
  --syn-k: #b0274a;
  --syn-t: #1d6e3a;
  --syn-f: #1c4fae;
  --syn-s: #84540a;
  --syn-c: #5f676c;
}

.dark .home-page {
  --code-bg: #161616;
  --syn-k: #ff7085;
  --syn-t: #8eef97;
  --syn-f: #7cb2ff;
  --syn-s: #ffeda1;
  --syn-c: #9da2a8;
}

.why-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20px;
  margin-top: 48px;
}

.why-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
  padding: 28px;
  border: 1px solid var(--ed-seam);
  border-radius: 12px;
  background: var(--ed-dark-1);
}

.why-card.wide {
  grid-column: 1 / -1;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 20px 40px;
  align-items: center;
}

.why-wide-text {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
}

.why-card.wide .why-tree {
  margin: 0;
}

.why-card h3 {
  margin: 0;
  font-size: 21px;
  line-height: 1.3;
  font-weight: 700;
}

.why-card p {
  margin: 10px 0 20px;
  font-size: 16px;
  color: var(--ed-text-2);
}

.why-code,
.why-tree {
  margin: 0 0 20px;
  padding: 16px;
  border: 1px solid var(--ed-seam);
  border-radius: 8px;
  background: var(--code-bg);
  font-family: var(--vp-font-family-mono);
  font-size: 13.5px;
  line-height: 1.65;
  color: var(--ed-text-1);
}

.why-code {
  overflow-x: auto;
}

.why-code .k { color: var(--syn-k); }
.why-code .t { color: var(--syn-t); }
.why-code .f { color: var(--syn-f); }
.why-code .s { color: var(--syn-s); }
.why-code .c { color: var(--syn-c); font-style: italic; }

.why-tree {
  list-style: none;
}

.why-tree li {
  display: flex;
  align-items: center;
  gap: 8px;
  padding-left: calc(var(--depth) * 22px);
}

.why-tree li.host {
  color: var(--ed-text-2);
}

.why-tree-glyph {
  flex: none;
  width: 16px;
  height: 16px;
  background-color: var(--ed-text-2);
  -webkit-mask: var(--gd-icon) no-repeat center / contain;
  mask: var(--gd-icon) no-repeat center / contain;
}

.why-tree-glyph.folder {
  background-color: var(--ed-folder);
}

.why-tree-note {
  margin-left: auto;
  font-size: 12px;
  color: var(--ed-text-2);
}

.why-link {
  margin-top: auto;
  font-weight: 600;
  color: var(--ed-accent);
  text-decoration: none;
}

.why-link:hover {
  text-decoration: underline;
}

@media (max-width: 1023px) {
  .why-grid,
  .why-card.wide {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 719px) {
  .why-grid {
    margin-top: 32px;
  }

  .why-card {
    padding: 20px;
  }
}
</style>
