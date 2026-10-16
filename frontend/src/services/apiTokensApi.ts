import { request, requestJson, send } from '@/services/apiCore'

/** As the API's ApiTokenLimits.NameMaxLength. */
export const API_TOKEN_NAME_MAX_LENGTH = 100

/** A read-only API token as the list shows it: never the token itself, only how it starts. */
export interface ApiToken {
  id: number
  name: string
  /** The token's first characters, "gs_" and six more. */
  prefix: string
  createdAt: string
  lastUsedAt: string | null
}

/** A token just made: `token` is shown this once and never again. */
export interface CreatedApiToken {
  details: ApiToken
  token: string
}

export const apiTokensApi = {
  list: () => request<ApiToken[]>('/api/tokens'),
  create: (name: string) => requestJson<CreatedApiToken>('/api/tokens', 'POST', { name }),
  revoke: (id: number) => send(`/api/tokens/${id}`, 'DELETE'),
}
