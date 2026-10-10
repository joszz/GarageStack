<template>
  <div class="chart-container">
    <div class="skeleton skeleton--text skeleton--text-md chart-skel__title"></div>
    <div class="skeleton chart-skel__body"></div>
  </div>
</template>

<style scoped>
/* Stands in for the card title, the same way the overview and location skeletons do. */
.chart-skel__title {
  height: 0.85rem;
  margin-bottom: 1rem;
}

/* aspect-ratio matches Chart.js aspectRatio: 2.6 so no height jump on load */
.chart-skel__body {
  width: 100%;
  aspect-ratio: 2.6;
}
</style>
