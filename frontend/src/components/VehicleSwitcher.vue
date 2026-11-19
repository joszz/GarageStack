<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import type { Vehicle } from '@/services/vehicleApi'

/**
 * The car every view follows. With one car on the account it is just the car's name; with more,
 * a picker. Choosing another car drops the previous one's data (see selectVehicle) and the views
 * load the new one's, since App keys the routed view on the active VIN.
 */
const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const store = useVehicleStore()

function baseName(v: Vehicle): string {
  return v.model ?? v.series ?? v.vin
}

// Two cars of the same model are told apart by the end of their VIN.
const options = computed(() => {
  const counts = new Map<string, number>()
  for (const v of store.vehicles) counts.set(baseName(v), (counts.get(baseName(v)) ?? 0) + 1)
  return store.vehicles.map((v) => {
    const name = baseName(v)
    const label =
      (counts.get(name) ?? 0) > 1 && name !== v.vin ? `${name} (…${v.vin.slice(-6)})` : name
    return { vin: v.vin, label }
  })
})

const activeLabel = computed(
  () => options.value.find((o) => o.vin === store.activeVin)?.label ?? null,
)

async function onChange(event: Event) {
  const vin = (event.target as HTMLSelectElement).value
  store.selectVehicle(vin)
  // A trip being edited belongs to the car just left.
  if (route.name === 'tripEdit') await router.push({ name: 'tripLog' })
}
</script>

<template>
  <div v-if="store.hasMultipleVehicles" class="sidebar-car-model vehicle-switcher">
    <font-awesome-icon icon="car" aria-hidden="true" />
    <select
      class="form-select vehicle-switcher__select"
      :value="store.activeVin ?? ''"
      :aria-label="t('nav.vehicle')"
      @change="onChange"
    >
      <option v-for="o in options" :key="o.vin" :value="o.vin">{{ o.label }}</option>
    </select>
  </div>
  <div v-else-if="activeLabel" class="sidebar-car-model">
    <font-awesome-icon icon="car" />
    <span>{{ activeLabel }}</span>
  </div>
</template>

<style scoped>
.vehicle-switcher {
  padding-block: 0.25rem;
}

.vehicle-switcher__select {
  flex: 1;
  min-width: 0;
  font-size: 0.85rem;
  font-weight: 600;
}
</style>
