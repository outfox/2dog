<!-- Two claims, each shown rather than told: the host loop and the xUnit test are trimmed from the
     template's own Program.cs and BasicTests.cs. -->
<script setup lang="ts">
import { withBase } from 'vitepress'
import { iconUrl } from './icons'
</script>

<template>
  <section class="home-section" id="love-dogs-and-robots" aria-labelledby="love-dogs-and-robots-title">
    <div class="home-wrap">
      <p class="home-kicker">
        <span class="gd-icon" :style="{ '--gd-icon': iconUrl('paw_print') }" aria-hidden="true"></span>2dog is Godot backwards
      </p>
      <h2 id="love-dogs-and-robots-title" class="home-h2">Love, Dogs + Robots<a class="home-anchor" href="#love-dogs-and-robots" aria-label="Permalink to this section">#</a></h2>
      <p class="home-intro">
        In addition to the pre-made hosts, you get unlimited customization options.
      </p>
      <div class="why-grid">
        <article class="why-card">
          <h3>You can have a custom Main Loop</h3>
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
          <h3>Test it in your IDE or Test Platform</h3>
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
      </div>
    </div>
  </section>
</template>

<!--suppress CssUnusedSymbol -->
<style>
.home-page {
  --code-bg: var(--ed-raise);
  --syn-k: #b0274a;
  --syn-t: #1d6e3a;
  --syn-f: #1c4fae;
  --syn-s: #84540a;
  --syn-c: var(--ed-text-3);
}

.dark .home-page {
  --code-bg: var(--ed-dark-2);
  --syn-k: #ff7085;
  --syn-t: #8eef97;
  --syn-f: #7cb2ff;
  --syn-s: #ffeda1;
  --syn-c: var(--ed-text-3);
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

.why-code {
  margin: 0 0 20px;
  padding: 16px;
  border: 1px solid var(--ed-seam);
  border-radius: 8px;
  background: var(--code-bg);
  font-family: var(--vp-font-family-mono),monospace;
  font-size: 14px;
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
  .why-grid {
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
