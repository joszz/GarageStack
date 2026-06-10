<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { usePwaInstall } from '@/composables/usePwaInstall'

const { t } = useI18n()
const { modalVisible, install, dismiss } = usePwaInstall()

const neverShow = ref(false)

function onDismiss() {
  dismiss(neverShow.value)
}
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div v-if="modalVisible" class="detail-modal-backdrop" @click.self="onDismiss">
        <div
          class="detail-modal pwa-install-modal"
          role="dialog"
          :aria-modal="true"
          :aria-label="t('pwa.install.title')"
        >
          <div class="pwa-install-modal__icon" aria-hidden="true">
            <img src="/pwa-192x192.png" :alt="t('pwa.install.iconAlt')" />
          </div>

          <div class="pwa-install-modal__content">
            <h2 class="pwa-install-modal__title">{{ t('pwa.install.title') }}</h2>
            <p class="pwa-install-modal__desc">{{ t('pwa.install.description') }}</p>

            <ul class="pwa-install-modal__features">
              <li>
                <font-awesome-icon icon="bolt" class="pwa-install-modal__feature-icon" />
                {{ t('pwa.install.featureOffline') }}
              </li>
              <li>
                <font-awesome-icon icon="bell" class="pwa-install-modal__feature-icon" />
                {{ t('pwa.install.featureNotifications') }}
              </li>
              <li>
                <font-awesome-icon icon="mobile-screen" class="pwa-install-modal__feature-icon" />
                {{ t('pwa.install.featureNative') }}
              </li>
            </ul>
          </div>

          <div class="pwa-install-modal__actions">
            <button class="btn btn-primary pwa-install-modal__cta" @click="install">
              <font-awesome-icon icon="download" />
              {{ t('pwa.install.install') }}
            </button>
            <button class="btn btn-outline-secondary" @click="onDismiss">
              {{ t('pwa.install.notNow') }}
            </button>
          </div>

          <label class="pwa-install-modal__never">
            <input v-model="neverShow" type="checkbox" />
            <span>{{ t('pwa.install.neverShow') }}</span>
          </label>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.pwa-install-modal {
  text-align: center;
  padding: 2rem 1.5rem 1.5rem;
}

.pwa-install-modal__icon {
  margin-bottom: 1rem;
}

.pwa-install-modal__icon img {
  width: 72px;
  height: 72px;
  border-radius: 18px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
}

.pwa-install-modal__content {
  margin-bottom: 1.5rem;
}

.pwa-install-modal__title {
  font-size: 1.2rem;
  font-weight: 700;
  margin: 0 0 0.5rem;
  color: var(--color-text);
}

.pwa-install-modal__desc {
  font-size: 0.9rem;
  color: var(--color-text-muted);
  margin: 0 0 1rem;
  line-height: 1.5;
}

.pwa-install-modal__features {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  text-align: left;
}

.pwa-install-modal__features li {
  font-size: 0.85rem;
  color: var(--color-text-muted);
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.pwa-install-modal__feature-icon {
  color: var(--color-primary);
  width: 14px;
  flex-shrink: 0;
}

.pwa-install-modal__actions {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.pwa-install-modal__cta {
  font-size: 0.95rem;
  padding: 0.65rem 1.5rem;
  justify-content: center;
  gap: 0.5rem;
}

.pwa-install-modal__never {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  margin-top: 1rem;
  font-size: 0.8rem;
  color: var(--color-text-muted);
  cursor: pointer;
  user-select: none;
}

.pwa-install-modal__never input[type='checkbox'] {
  accent-color: var(--color-primary);
  cursor: pointer;
}

@media (min-width: 768px) {
  .pwa-install-modal {
    max-width: 380px;
  }

  .pwa-install-modal__actions {
    flex-direction: row;
    justify-content: center;
  }

  .pwa-install-modal__cta {
    flex: 1;
  }
}
</style>
