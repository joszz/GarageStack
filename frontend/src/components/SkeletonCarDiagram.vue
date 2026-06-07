<template>
  <div class="tyre-diagram">
    <div class="tyre-skel__title skeleton skeleton--text skeleton--text-sm"></div>
    <div class="tyre-diagram__wrap">
      <div class="tyre-skel__car skeleton"></div>
    </div>
    <div class="tyre-skel__legend">
      <div class="skeleton skeleton--text tyre-skel__legend-item"></div>
      <div class="skeleton skeleton--text tyre-skel__legend-item"></div>
      <div class="skeleton skeleton--text tyre-skel__legend-item"></div>
    </div>
  </div>
</template>

<style scoped>
.tyre-skel__title {
  height: 0.85rem;
  margin-bottom: 1rem;
  max-width: 5rem;
}
.tyre-skel__car {
  width: 220px;
  max-width: 100%;
  aspect-ratio: 340 / 480;
  border-radius: var(--radius);
}
.tyre-skel__legend {
  display: flex;
  gap: 1rem;
  margin-top: 1rem;
  flex-wrap: wrap;
}
.tyre-skel__legend-item {
  height: 0.9rem;
  width: 5rem;
  border-radius: 4px;
}
</style>
