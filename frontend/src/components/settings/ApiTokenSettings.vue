<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { API_TOKEN_NAME_MAX_LENGTH, apiTokensApi, type ApiToken } from '@/services/apiTokensApi'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { useTwoTapConfirm } from '@/composables/useTwoTapConfirm'
import { formatDateTime } from '@/utils/format'

// Read-only tokens for scripts, dashboards and gadgets. A new token is shown once, to be copied;
// the list only ever shows how each one starts.
const { t } = useI18n()
const errorMessage = useErrorMessage()

const tokens = ref<ApiToken[]>([])
const name = ref('')
const created = ref<string | null>(null)
const copied = ref(false)
const error = ref<string | null>(null)
const busy = ref(false)
const { armed: confirmingId, tap: tapRevoke } = useTwoTapConfirm<number>()

onMounted(async () => {
  try {
    tokens.value = await apiTokensApi.list()
  } catch (e) {
    error.value = errorMessage(e)
  }
})

async function create() {
  const trimmed = name.value.trim()
  if (!trimmed || busy.value) return
  error.value = null
  busy.value = true
  try {
    const result = await apiTokensApi.create(trimmed)
    created.value = result.token
    copied.value = false
    name.value = ''
    tokens.value = [result.details, ...tokens.value]
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}

async function copy() {
  if (!created.value) return
  try {
    await navigator.clipboard.writeText(created.value)
    copied.value = true
  } catch {
    // No clipboard (plain HTTP, or the browser said no): the field is there to copy by hand.
  }
}

// The first tap asks, the second revokes: a script using the token stops working at once.
async function revoke(id: number) {
  if (!tapRevoke(id)) return
  error.value = null
  try {
    await apiTokensApi.revoke(id)
    tokens.value = tokens.value.filter((token) => token.id !== id)
  } catch (e) {
    error.value = errorMessage(e)
  }
}

function usage(token: ApiToken): string {
  const createdText = t('settings.apiTokens.createdAt', { date: formatDateTime(token.createdAt) })
  const usedText = token.lastUsedAt
    ? t('settings.apiTokens.lastUsed', { date: formatDateTime(token.lastUsedAt) })
    : t('settings.apiTokens.neverUsed')
  return `${createdText} · ${usedText}`
}
</script>

<template>
  <div class="form-stack">
    <p class="settings-toggle__desc">{{ t('settings.apiTokens.desc') }}</p>

    <form class="api-token-form" @submit.prevent="create">
      <input
        v-model="name"
        class="form-control"
        :maxlength="API_TOKEN_NAME_MAX_LENGTH"
        :placeholder="t('settings.apiTokens.namePlaceholder')"
        :aria-label="t('settings.apiTokens.nameLabel')"
      />
      <button type="submit" class="btn btn-primary btn-sm" :disabled="busy || !name.trim()">
        {{ t('settings.apiTokens.create') }}
      </button>
    </form>

    <div v-if="created" class="form-stack" role="status">
      <p class="text-sm">{{ t('settings.apiTokens.copyNow') }}</p>
      <div class="api-token-form">
        <input
          class="form-control"
          readonly
          :value="created"
          :aria-label="t('settings.apiTokens.newToken')"
          @focus="($event.target as HTMLInputElement).select()"
        />
        <button type="button" class="btn btn-outline-secondary btn-sm" @click="copy">
          <font-awesome-icon :icon="copied ? 'check' : 'copy'" />
          {{ copied ? t('settings.apiTokens.copied') : t('settings.apiTokens.copy') }}
        </button>
      </div>
    </div>

    <p v-if="error" class="text-danger text-sm" role="alert">{{ error }}</p>

    <div v-if="tokens.length" class="detail-list">
      <div v-for="token in tokens" :key="token.id" class="detail-list__item">
        <font-awesome-icon icon="key" class="detail-list__item-icon" />
        <div class="api-token-row">
          <span class="detail-list__item-label">{{ token.name }}</span>
          <span class="settings-toggle__desc">{{ token.prefix }}… · {{ usage(token) }}</span>
        </div>
        <button
          type="button"
          class="btn btn-sm"
          :class="confirmingId === token.id ? 'btn-danger' : 'btn-outline-secondary'"
          :aria-label="
            t(
              confirmingId === token.id
                ? 'settings.apiTokens.confirmRevokeNamed'
                : 'settings.apiTokens.revokeNamed',
              { name: token.name },
            )
          "
          @click="revoke(token.id)"
        >
          {{
            confirmingId === token.id
              ? t('settings.apiTokens.confirmRevoke')
              : t('settings.apiTokens.revoke')
          }}
        </button>
      </div>
    </div>
    <p v-else class="settings-toggle__desc">{{ t('settings.apiTokens.none') }}</p>
  </div>
</template>
