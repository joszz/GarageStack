<template>
  <div class="chart-container">
    <div class="skeleton skeleton--text skeleton--text-md chart-skel__title"></div>
    <div class="skeleton chart-skel__body"></div>
  </div>
</template>

<style scoped>
/* Height matches h2 font-size (1rem) × base line-height (1.6) + same margin-bottom as .chart-container h2 */
.chart-skel__title {
  height: 1.6rem;
  margin-bottom: 1rem;
}
/* aspect-ratio matches Chart.js aspectRatio: 2.6 so no height jump on load */
.chart-skel__body {
  width: 100%;
  aspect-ratio: 2.6;
}
</style>
