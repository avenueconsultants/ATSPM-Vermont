import { getEnv } from '@/utils/getEnv'
import { buildExternalLoginUrl, getVisibleSsoProviders } from './ssoProviders'

export async function loadVisibleSsoProviders() {
  const env = await getEnv()
  return getVisibleSsoProviders(env)
}

export async function redirectToExternalLogin(providerKey: string) {
  const env = await getEnv()
  const externalLoginUrl = buildExternalLoginUrl(env, providerKey)

  window.open(externalLoginUrl, '_self')
}
