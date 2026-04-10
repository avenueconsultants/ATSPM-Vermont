// #region license
// Copyright 2026 Utah Departement of Transportation
// for WebUI - ssoProviders.test.ts
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// #endregion
import type { EnvVariables } from '@/utils/getEnv'
import { buildExternalLoginUrl, getVisibleSsoProviders } from './ssoProviders'

describe('ssoProviders', () => {
  it('returns only recognized providers in configured order', () => {
    const env = {
      SSO_VISIBLE_PROVIDERS: ' ENTRA, unknown, utahid ',
    } as EnvVariables

    expect(getVisibleSsoProviders(env)).toEqual([
      {
        key: 'entra',
        label: 'Sign in with Microsoft Entra',
      },
      {
        key: 'utahid',
        label: 'Sign in with Utah Id',
      },
    ])
  })

  it('returns no providers when configuration is missing', () => {
    expect(getVisibleSsoProviders(null)).toEqual([])
    expect(getVisibleSsoProviders({} as EnvVariables)).toEqual([])
    expect(
      getVisibleSsoProviders({
        SSO_VISIBLE_PROVIDERS: '',
      } as EnvVariables)
    ).toEqual([])
  })

  it('builds the external login URL without duplicating the api version segment', () => {
    const env = {
      IDENTITY_URL: 'https://identity.example.com/api/v1/',
    } as EnvVariables

    expect(buildExternalLoginUrl(env, 'entra')).toBe(
      'https://identity.example.com/api/v1/Account/external-login?provider=entra'
    )
  })
})
