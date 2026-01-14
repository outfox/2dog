<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'

interface Slogan {
  name: string
  text: string
}

const slogans: Slogan[] = [
  { name: "What if Godot...", text: "...but backward?" },
  { name: "Let's take Godot...", text: "...for walkies!" },
  { name: "Who's a good engine?", text: "Godot is!" },
  { name: "Sit, Godot!", text: "Good engine. Now render." },
  { name: "Teaching old Godot...", text: "...new .NET tricks" },
  { name: "Godot? More like...", text: "Go-do-whatever-you-want!" },
  { name: "Every Robot deserves...", text: "...a good .NET home" },
  { name: "Fetch the scene tree!", text: "Good Godot!" },
  { name: "No more waiting...", text: "...for Godot" },
  { name: "Your Godot...", text: "...your rules" },
  { name: "Godot heel!", text: "Now we're in charge." },
  { name: "Roll over, Godot!", text: "Time to run .NET side up" },
  { name: "Unit tests?", text: "A walk in the park!" },
  { name: "Adopt, don't export!", text: "Take Godot home today" },
  { name: "We put the 'woof'...", text: "...in workflow!" },
]

function getRandomIndex() {
  return Math.floor(Math.random() * slogans.length)
}

const currentIndex = ref(getRandomIndex())
let intervalId: number | undefined

const currentSlogan = computed(() => slogans[currentIndex.value])

onMounted(() => {
  intervalId = window.setInterval(() => {
    currentIndex.value = getRandomIndex()
  }, 6000) // Rotate every x seconds
})

onUnmounted(() => {
  if (intervalId !== undefined) {
    window.clearInterval(intervalId)
  }
})
</script>

<template>
  <div class="hero-carousel">
    <Transition name="fade" mode="out-in">
      <div :key="currentIndex" class="slogan-container">
        <h1 class="name">
          <span class="clip">{{ currentSlogan.name }}</span>
        </h1>
        <p class="text">{{ currentSlogan.text }}</p>
      </div>
    </Transition>
    <p class="tagline">Start &amp; control Godot from .NET code.</p>
  </div>
</template>

<style scoped>
.hero-carousel {
  text-align: left;
}

.slogan-container {
  min-height: 160px;
  display: flex;
  flex-direction: column;
  justify-content: center;
}

.name {
  font-size: 48px;
  line-height: 1.2;
  font-weight: 700;
  margin: 0;
  letter-spacing: -0.02em;
}

.clip {
  background: linear-gradient(120deg, var(--vp-c-brand-1) 30%, var(--vp-c-brand-2));
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
}

.text {
  font-size: 48px;
  line-height: 1.2;
  font-weight: 700;
  margin: 8px 0 0;
  color: var(--vp-c-text-1);
  letter-spacing: -0.02em;
}

.tagline {
  font-size: 20px;
  color: var(--vp-c-text-2);
  margin-top: 24px;
  padding: 0 20px;
}

/* Fade transition */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.4s ease, transform 0.4s ease;
}

.fade-enter-from {
  opacity: 0;
  transform: translateY(10px);
}

.fade-leave-to {
  opacity: 0;
  transform: translateY(-10px);
}

/* Responsive */
@media (max-width: 959px) {
  .hero-carousel {
    text-align: center;
  }

  .name {
    font-size: 40px;
  }
  .text {
    font-size: 40px;
  }
  .slogan-container {
    min-height: 140px;
  }
}

@media (max-width: 639px) {
  .name {
    font-size: 32px;
  }
  .text {
    font-size: 32px;
  }
  .tagline {
    font-size: 18px;
  }
  .slogan-container {
    min-height: 120px;
  }
}
</style>
