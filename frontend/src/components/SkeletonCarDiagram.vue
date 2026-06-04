<template>
  <div class="tyre-diagram">
    <div class="tyre-skel__title skeleton skeleton--text skeleton--text-sm"></div>
    <div class="tyre-diagram__wrap">
      <div class="tyre-skel__car skeleton"></div>
    </div>
  </div>
</template>

<style scoped>
.tyre-skel__title {
  height: 0.85rem;
  margin-bottom: 0.75rem;
  max-width: 5rem;
}
.tyre-skel__car {
  width: 200px;
  max-width: 100%;
  aspect-ratio: 220 / 340;
  border-radius: var(--radius);
}
</style>
