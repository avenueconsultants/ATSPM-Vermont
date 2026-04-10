# SSO Provider Design

## Summary

This document describes the planned update to ATSPM authentication so the application can support Microsoft Entra / Azure AD in addition to the existing external OpenID Connect provider. The change also adds a frontend configuration option to control which SSO providers are shown on the login page.

The scope of this design is limited to external login provider selection, provider configuration, frontend visibility, and preserving the current ATSPM JWT-based authorization flow after SSO succeeds.

## Current State

The current implementation uses a single external OIDC configuration and a single external login flow:

- `IdentityApi` reads one `Oidc` configuration block.
- `AuthenticationExtensions` registers one OpenID Connect scheme using `OpenIdConnectDefaults.AuthenticationScheme`.
- `AccountController` challenges that one scheme through `GET /api/v1/Account/external-login`.
- The WebUI login page shows one hard-coded SSO button labeled `Sign in with Utah Id`.
- After the external provider authenticates the user, the callback creates or links the ATSPM user, issues an ATSPM JWT, and redirects the browser to `/sso-login`.

This works for one provider, but it does not support adding Microsoft Entra alongside the existing provider or controlling which provider buttons are visible in the frontend.

## Goals

- Support Microsoft Entra / Azure AD as an additional SSO provider.
- Preserve the existing external provider.
- Allow the backend to register multiple OIDC providers.
- Allow the frontend to show only a configured subset of providers.
- Keep ATSPM JWT generation, account linking, and authorization behavior unchanged after external authentication succeeds.

## Non-Goals

- Replace the existing SSO provider.
- Add external group-to-role or external role-to-claim mapping.
- Add a backend provider discovery endpoint in this phase.
- Change ATSPM authorization policies or JWT validation semantics.

## Target Design

### Backend Authentication Model

The backend will move from a single `Oidc` configuration block to a multi-provider configuration model. Each configured provider will define:

- Provider key
- Scheme name
- Display name
- Authority
- Client ID
- Client secret
- Callback path
- Scopes

`AuthenticationExtensions` will register one OpenID Connect scheme per configured provider while keeping JWT bearer as the default API authentication scheme.

This allows the identity service to support both:

- the existing external provider
- Microsoft Entra / Azure AD

### Provider Selection

`GET /api/v1/Account/external-login` will accept a `provider` query parameter. The controller will:

- validate the requested provider key
- resolve the matching authentication scheme
- challenge that scheme

If the provider is unknown or not configured, the endpoint should return `400 Bad Request`.

### Callback Flow

The callback remains shared. After the external provider returns to ATSPM:

1. ATSPM retrieves the external login info.
2. ATSPM resolves the incoming provider and external identity.
3. ATSPM creates a local user if one does not exist.
4. ATSPM links the external login if the user already exists locally but is not yet linked.
5. ATSPM generates the ATSPM JWT.
6. ATSPM redirects the browser to `/sso-login` with the token and claim payload.

This preserves the existing application-level authorization model.

Each provider must define a unique `CallbackPath`. ASP.NET Core's OIDC middleware handles the redirect from the external IdP directly to the registered callback path — it does not go through the `OIDCLoginCallback` controller action. The controller action is only needed for post-authentication work (JWT issuance, redirect to `/sso-login`). The shared post-authentication logic must be wired via the `OnTicketReceived` or `OnTokenValidated` event in `AuthenticationExtensions`, not via a single controller route, so that all providers funnel into the same handler regardless of their individual callback paths.

## Microsoft Entra Support

Microsoft Entra will be implemented as another configured OpenID Connect provider, not as a special-case authentication path.

The implementation should support the standard Entra values for:

- tenant-specific authority
- client ID
- client secret
- callback path
- standard scopes such as `openid`, `profile`, and `email`

### Claim Normalization

Entra issues claims using different names than the .NET `ClaimTypes` URN format that `AccountService` currently reads. For example, Entra uses `preferred_username` or `email` instead of `ClaimTypes.Email`, and `given_name` / `family_name` instead of `ClaimTypes.GivenName` / `ClaimTypes.Surname`.

Normalization must happen in the `OnTokenValidated` event handler inside `AuthenticationExtensions`, not in `AccountService`. This keeps `AccountService` provider-agnostic: it always receives a consistent set of claims regardless of which IdP authenticated the user. The event handler for each Entra scheme must map incoming Entra claims to the standard .NET claim types before the ticket is accepted.

The `app:Atspm` custom scope is Utah-specific and should not be requested for the Entra provider. Scopes are per-provider configuration.

## Frontend Design

### Visible Provider Configuration

The WebUI will use frontend environment configuration to decide which SSO providers to display.

A new frontend environment variable will be added:

```text
SSO_VISIBLE_PROVIDERS=utahid,entra
```

This value is a comma-separated list of provider keys. The login page will render only providers included in this list.

### Login Page Behavior

The login page will no longer hard-code a single external provider button. Instead, it will render buttons from a local provider definition map that contains:

- provider key
- button label
- backend query parameter value

Example visible buttons:

- `Sign in with Utah Id`
- `Sign in with Microsoft Entra`

Clicking a provider button will navigate to:

```text
/api/v1/Account/external-login?provider=<provider-key>
```

### Provider Key Contract

The keys used in `SSO_VISIBLE_PROVIDERS` must exactly match the provider keys defined in the backend `OidcProviders` configuration and passed as the `provider=` query parameter to `external-login`. These keys are a shared contract across backend config, frontend env, and the API surface. A key change in any one place requires a coordinated update in all three.

### Empty Provider List Behavior

If `SSO_VISIBLE_PROVIDERS` is missing, empty, or contains no recognized keys, the login page must hide all SSO provider buttons. It must not show an error state. Local login (if applicable) remains unaffected.

### Why Frontend Env Is Used

Frontend environment-driven visibility is the simplest implementation for this phase because it:

- avoids adding a new backend provider discovery API
- allows deployments to hide providers without changing code
- keeps frontend display decisions separate from backend provider registration

## Configuration Changes

### Backend

The single-provider `Oidc` configuration will be replaced by a named dictionary under `OidcProviders`. Each key is the provider key used in both the `provider=` query parameter and the frontend `SSO_VISIBLE_PROVIDERS` list. Example:

```json
"OidcProviders": {
  "utahid": {
    "DisplayName": "Utah Id",
    "Authority": "https://login.utah.gov/...",
    "ClientId": "...",
    "ClientSecret": "...",
    "CallbackPath": "/signin-utahid",
    "Scopes": ["openid", "email", "profile", "app:Atspm"]
  },
  "entra": {
    "DisplayName": "Microsoft Entra",
    "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
    "ClientId": "...",
    "ClientSecret": "...",
    "CallbackPath": "/signin-entra",
    "Scopes": ["openid", "email", "profile"]
  }
}
```

`AuthenticationExtensions` will iterate over `OidcProviders` and register one OIDC scheme per entry. By default, the scheme name will be derived from the provider key (e.g., `oidc-utahid`, `oidc-entra`). An optional `Scheme` override may be supplied for compatibility with existing provider naming, but the derived `oidc-{providerKey}` convention remains the default.

A typed `OidcProviderConfiguration` class must be introduced to bind these values. The existing untyped `IConfiguration` reads in `AuthenticationExtensions` must be replaced.

### Provider Validation

`AccountController.ExternalLogin` must validate the incoming `provider` query parameter against the registered provider keys. The valid key list should be sourced from `IAuthenticationSchemeProvider` or from a registered `IOptions<Dictionary<string, OidcProviderConfiguration>>` — not hardcoded. If the provider is not found, return `400 Bad Request`.

### Frontend

The frontend environment will gain:

```text
SSO_VISIBLE_PROVIDERS=utahid,entra
```

If a provider is not listed, the corresponding login button should not render.

## API and Interface Changes

### External Login Endpoint

Current behavior:

```text
GET /api/v1/Account/external-login
```

Target behavior:

```text
GET /api/v1/Account/external-login?provider=utahid
GET /api/v1/Account/external-login?provider=entra
```

Behavioral expectations:

- valid provider: challenge matching OIDC scheme
- invalid provider: return `400 Bad Request`

## Testing Expectations

### Backend Tests

- Multiple OIDC providers register successfully when configured.
- `external-login` challenges the correct scheme for each valid provider.
- `external-login` returns `400` for an unknown provider.
- Microsoft Entra claims create a new ATSPM user when none exists.
- Microsoft Entra login links to an existing ATSPM user when email matches.
- Already linked Entra users can sign in successfully.

### Frontend Tests

- Only providers listed in `SSO_VISIBLE_PROVIDERS` are shown.
- Clicking a provider button navigates to the correct external-login URL.
- Missing or empty provider configuration results in no SSO buttons or a clearly defined fallback behavior.

### End-to-End Verification

- Existing external provider still works.
- Microsoft Entra login works.
- `/sso-login` still stores cookies and redirects correctly.
- ATSPM-authenticated API calls continue using the ATSPM JWT after SSO completes.
- `external-login?provider=unknown` returns `400 Bad Request`.

## Assumptions

- The existing external provider remains supported.
- Microsoft Entra is added as a second provider, not a replacement.
- Provider visibility is controlled by frontend environment configuration in this phase.
- ATSPM continues to manage its own JWTs and authorization after external authentication succeeds.
- External IdP group and role mapping is out of scope for this phase.
