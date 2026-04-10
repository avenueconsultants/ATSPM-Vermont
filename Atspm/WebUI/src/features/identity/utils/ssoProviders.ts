import { buildApiUrl } from '@/lib/axios'
import { EnvVariables } from '@/utils/getEnv'

export type SsoProviderDefinition = {
  key: string
  label: string
}

const AVAILABLE_PROVIDERS: Record<string, SsoProviderDefinition> = {
  utahid: {
    key: 'utahid',
    label: 'Sign in with Utah Id',
  },
  entra: {
    key: 'entra',
    label: 'Sign in with Microsoft Entra',
  },
}

export function getVisibleSsoProviders(
  env: EnvVariables | null | undefined
): SsoProviderDefinition[] {
  const visibleProviders = env?.SSO_VISIBLE_PROVIDERS
    ?.split(',')
    .map((provider) => provider.trim().toLowerCase())
    .filter(Boolean)

  if (!visibleProviders?.length) {
    return []
  }

  return visibleProviders
    .map((providerKey) => AVAILABLE_PROVIDERS[providerKey])
    .filter((provider): provider is SsoProviderDefinition => !!provider)
}

export function buildExternalLoginUrl(
  env: EnvVariables | null | undefined,
  providerKey: string
): string {
  return buildApiUrl(
    env?.IDENTITY_URL,
    `/api/v1/Account/external-login?provider=${encodeURIComponent(providerKey)}`
  )
}
