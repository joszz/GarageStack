<script setup lang="ts">
import { toRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ChargeSession } from '@/services/vehicleApi'
import type { ChargeEnergyContext } from '@/utils/chargeSessions'
import { measureText, useChargeSession } from '@/composables/useChargeSession'

const props = defineProps<{
  session: ChargeSession
  energyContext: ChargeEnergyContext
  /** The session the detail shows. */
  selected: boolean
}>()

/** Asks for this session's detail: beside the list, or in a dialog on a narrower screen. */
const emit = defineEmits<{ (e: 'select'): void }>()

const { t } = useI18n()
const {
  ongoing,
  icon,
  badgeClass,
  kindLabel,
  shortDate,
  startTime,
  endTime,
  duration,
  stateOfCharge,
  maxPower,
  gained,
} = useChargeSession(toRef(props, 'session'), toRef(props, 'energyContext'))
</script>

<template>
  <li class="list-row charge-history-item" :class="{ 'list-row--selected': selected }">
    <!-- Laid out as a trip log row, two lines at most: the kind of charge down the left, when on
         the first line, how far it charged on the second, and the energy on the right. The
         detail has the rest. -->
    <button type="button" class="list-row__select" :aria-pressed="selected" @click="emit('select')">
      <span
        class="list-row__icon badge"
        :class="badgeClass"
        role="img"
        :title="kindLabel"
        :aria-label="kindLabel"
      >
        <font-awesome-icon :icon="icon" />
      </span>

      <span class="list-row__when">
        <span class="list-row__date">{{ shortDate }}</span>
        <span class="list-row__time text-xs text-muted">
          {{ startTime }}<span v-if="endTime" class="list-row__end-time"> - {{ endTime }}</span>
        </span>
        <span v-if="duration" class="list-row__pill text-xs">{{ duration }}</span>
        <span v-else-if="ongoing" class="list-row__pill list-row__pill--active text-xs">{{
          t('chargeHistory.ongoing')
        }}</span>
      </span>

      <span
        v-if="gained"
        class="list-row__value charge-history-item__energy"
        :title="t('chargeHistory.batteryEnergyGained')"
        >{{ measureText(gained) }}</span
      >

      <span class="list-row__line charge-history-item__readings">
        <span
          v-if="stateOfCharge"
          class="charge-history-item__reading charge-history-item__soc"
          :title="t('chargeHistory.stateOfCharge')"
        >
          <font-awesome-icon
            icon="battery-half"
            role="img"
            :aria-label="t('chargeHistory.stateOfCharge')"
          />{{ stateOfCharge }}
        </span>
        <span
          v-if="maxPower"
          class="charge-history-item__reading charge-history-item__power"
          :title="t('chargeHistory.maxPower')"
        >
          <font-awesome-icon
            icon="gauge-high"
            role="img"
            :aria-label="t('chargeHistory.maxPower')"
          />{{ measureText(maxPower) }}
        </span>
      </span>

      <font-awesome-icon icon="chevron-right" class="list-row__chevron" aria-hidden="true" />
    </button>
  </li>
</template>
