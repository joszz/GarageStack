# Authentication

GarageStack has two ways to sign in:

1. **OpenID Connect (OIDC)** -- delegate sign-in to your own identity provider (Authentik, Authelia, Keycloak, Pocket ID, Zitadel, Google, ...). Recommended: user management, password policy, and two-factor stay where they belong, and GarageStack never sees a password.
2. **Built-in password login** -- a single username and password, meant as a fallback for installs that have no identity provider.

**Setting `OIDC_AUTHORITY` switches GarageStack to OIDC and turns the password login off**, so an SSO install has one way in by default. Set `AUTH_PASSWORD_LOGIN_ENABLED=true` to keep both available -- see [Running both at once](#running-both-at-once).

Either way the result is the same: an encrypted, HTTP-only session cookie. Logging out revokes that session server-side, not just in the browser, so a copied cookie stops working immediately.

---

## Quick start with OIDC

1. **Create a client (application) at your provider.** It needs the authorization code flow with PKCE, which is the default for every provider listed below.

2. **Register the redirect URI.** It is always your GarageStack URL plus a fixed path:

   ```
   https://garage.example.com/api/auth/oidc/callback
   ```

   Use the exact URL you open in the browser, including the port if you use one (`http://192.168.1.100:8080/api/auth/oidc/callback`).

3. **Fill in the variables** in `.env` (Docker Compose) or as container environment variables (all-in-one / Unraid):

   ```bash
   OIDC_AUTHORITY=https://auth.example.com/application/o/garagestack/
   OIDC_CLIENT_ID=<from your provider>
   OIDC_CLIENT_SECRET=<from your provider>
   OIDC_PROVIDER_NAME=Authentik
   OIDC_ALLOWED_GROUPS=garagestack-users
   ```

4. **Restart GarageStack.** The login page now shows a single "Sign in with ..." button.

The API logs a line at startup confirming what it picked up:

```
Authentication: OIDC via https://auth.example.com/application/o/garagestack/ (client garagestack, auto-login False)
```

---

## Environment variables

| Variable | Default | Description |
| --- | --- | --- |
| `OIDC_AUTHORITY` | _(empty)_ | Issuer URL of your provider: the URL its `/.well-known/openid-configuration` document sits under. Setting it enables OIDC. |
| `OIDC_CLIENT_ID` | _(empty)_ | Client ID issued by the provider. Required when `OIDC_AUTHORITY` is set. |
| `OIDC_CLIENT_SECRET` | _(empty)_ | Client secret. Leave empty for a public client (PKCE only). |
| `OIDC_SCOPES` | `openid profile email` | Space-separated scopes. Add the scope carrying group membership if your provider needs one (`groups` on Authelia and Pocket ID). `openid` is added automatically when missing. |
| `OIDC_PROVIDER_NAME` | `SSO` | Name on the sign-in button ("Sign in with Authentik"). |
| `OIDC_AUTO_LOGIN` | `false` | Skip the GarageStack login page and go straight to the provider. |
| `OIDC_ALLOWED_GROUPS` | _(empty)_ | Comma-separated groups allowed to sign in. Empty means no group check. |
| `OIDC_ALLOWED_EMAILS` | _(empty)_ | Comma-separated email addresses allowed to sign in. Empty means no email check. |
| `OIDC_GROUPS_CLAIM` | `groups` | Claim that `OIDC_ALLOWED_GROUPS` is matched against. |
| `OIDC_REDIRECT_URI` | _(derived)_ | Explicit redirect URI, for deployments where GarageStack cannot work out its own public URL. Must end in `/api/auth/oidc/callback`. |
| `OIDC_REQUIRE_HTTPS_METADATA` | `true` | Set to `false` only for a provider served over plain HTTP on your LAN. |
| `AUTH_USERNAME` | _(MG account)_ | Username for the built-in password login. Falls back to `SAIC_USER`. |
| `AUTH_PASSWORD` | _(MG account)_ | Password for the built-in password login. Falls back to `SAIC_PASSWORD`. |
| `AUTH_PASSWORD_LOGIN_ENABLED` | _(on without OIDC)_ | `true` keeps the password login available alongside OIDC, `false` switches it off entirely. Unset means "on only while no provider is configured". |
| `AUTH_COOKIE_SECURE` | `false` | Marks the session cookie `Secure`. Set to `true` when serving over HTTPS. |
| `AUTH_SESSION_LIFETIME_HOURS` | `168` | How long an OIDC session lasts before the provider is consulted again. |

`JWT_SECRET` is no longer used. See [Upgrading from the previous login](#upgrading-from-the-previous-login).

---

## Restrict who can sign in

> **This matters.** Without a restriction, **every account your provider accepts** can sign in to GarageStack -- and GarageStack can unlock your car, start climate control and see where it has been. With a public provider such as Google, "every account" means every Google account on the internet.

There are two places to restrict access, and using either one is enough:

- **At the provider** (preferred). Authentik binds a group or policy to the application, Authelia has `authorization_policy` per client, Keycloak has client roles. GarageStack then only ever sees users the provider already approved.
- **In GarageStack**, with `OIDC_ALLOWED_GROUPS` and/or `OIDC_ALLOWED_EMAILS`.

```bash
OIDC_ALLOWED_GROUPS=garagestack-users,admins
OIDC_ALLOWED_EMAILS=owner@example.com
```

Details of the check:

- A user gets in when they match **either** list. Matching is case-insensitive.
- Groups come from the `groups` claim (configurable with `OIDC_GROUPS_CLAIM`), read from the ID token or the userinfo response.
- An allow-listed email is refused if the provider explicitly reports `email_verified: false`. Providers that omit the claim entirely -- most self-hosted ones -- are taken at their word.
- With both lists empty, the API logs a warning at every start.

A rejected user lands back on the login page with "This account is not allowed to use GarageStack", and the reason is logged:

```
OIDC sign-in denied for mallory: groups [photos] do not match OIDC_ALLOWED_GROUPS
```

---

## Auto-login

With `OIDC_AUTO_LOGIN=true` the login page is skipped: opening GarageStack goes straight to the provider and, while the provider's own session is still valid, straight back in without a single click.

Logging out still works as you would expect. GarageStack ends its own session and returns you to the login page with auto-login suppressed for that visit, so you are not signed straight back in. Your provider session is left alone -- log out there (or in another app that supports single logout) to end it everywhere.

---

## Provider setup

The exact wording differs per version, but every provider needs the same three things: the authorization code flow, the redirect URI, and (usually) a group or policy limiting who may use the application.

### Authentik

1. **Applications > Providers > Create** > *OAuth2/OpenID Provider*.
   - Client type: **Confidential**
   - Redirect URI, strict: `https://garage.example.com/api/auth/oidc/callback`
   - Scopes: `openid`, `profile`, `email`
2. **Applications > Applications > Create**, bind the provider, and bind a group policy so only that group can use it.
3. The provider page shows **OpenID Configuration Issuer** -- that value is `OIDC_AUTHORITY`.

```bash
OIDC_AUTHORITY=https://auth.example.com/application/o/garagestack/
OIDC_PROVIDER_NAME=Authentik
OIDC_ALLOWED_GROUPS=garagestack-users
```

Authentik's default `profile` scope mapping emits a `groups` claim. If yours was customised and no groups arrive, add a scope mapping that returns them, or restrict access with the application's policy bindings instead.

### Authelia

In `configuration.yml`:

```yaml
identity_providers:
  oidc:
    clients:
      - client_id: garagestack
        client_name: GarageStack
        client_secret: '$pbkdf2-sha512$...'   # the hash; GarageStack gets the plaintext
        public: false
        authorization_policy: two_factor
        require_pkce: true
        pkce_challenge_method: S256
        token_endpoint_auth_method: client_secret_post
        redirect_uris:
          - https://garage.example.com/api/auth/oidc/callback
        scopes: [openid, profile, email, groups]
```

`token_endpoint_auth_method: client_secret_post` is not optional. ASP.NET Core always sends the client credentials in the request body, while Authelia defaults confidential clients to `client_secret_basic` and rejects everything else with `invalid_client`. Generate both halves of the secret in one go:

```bash
authelia crypto hash generate pbkdf2 --variant sha512 --random --random.length 72 --random.charset rfc3986
```

"Random Password" goes into `OIDC_CLIENT_SECRET`, "Digest" into `client_secret` above.

```bash
OIDC_AUTHORITY=https://auth.example.com
OIDC_SCOPES=openid profile email groups
OIDC_PROVIDER_NAME=Authelia
OIDC_ALLOWED_GROUPS=garagestack-users
```

Authelia keeps ID token claims minimal on recent versions; GarageStack reads the userinfo endpoint as well, so the `groups` scope is all that is needed.

### Keycloak

1. **Clients > Create client**: client type OpenID Connect, **Client authentication** on, **Standard flow** enabled.
2. Valid redirect URIs: `https://garage.example.com/api/auth/oidc/callback`
3. For groups, add a **Group Membership** mapper on a client scope with token claim name `groups` and "Full group path" off (otherwise the values arrive as `/garagestack-users`).

```bash
OIDC_AUTHORITY=https://keycloak.example.com/realms/home
OIDC_PROVIDER_NAME=Keycloak
OIDC_ALLOWED_GROUPS=garagestack-users
```

### Pocket ID

1. **OIDC Clients > Add client**, callback URL `https://garage.example.com/api/auth/oidc/callback`.
2. Allow the client for the group you want, and request the `groups` scope if you also want to check groups in GarageStack.

```bash
OIDC_AUTHORITY=https://id.example.com
OIDC_SCOPES=openid profile email groups
OIDC_PROVIDER_NAME=Pocket ID
```

### Google

Google has no groups, so **an email allow-list is mandatory** -- without one, any Google account can sign in.

1. Google Cloud console > **APIs & Services > Credentials > Create credentials > OAuth client ID**, type *Web application*.
2. Authorized redirect URI: `https://garage.example.com/api/auth/oidc/callback` (Google requires HTTPS).

```bash
OIDC_AUTHORITY=https://accounts.google.com
OIDC_ALLOWED_EMAILS=owner@example.com
OIDC_PROVIDER_NAME=Google
```

---

## Built-in password login

On by default while `OIDC_AUTHORITY` is empty. Credentials come from `AUTH_USERNAME` / `AUTH_PASSWORD`, falling back to the MG account (`SAIC_USER` / `SAIC_PASSWORD`) so existing installs keep working untouched.

Sessions last 12 hours, or 30 days with "remember me". Login is rate-limited to 10 attempts per 5 minutes per IP address.

Setting dedicated credentials is worth it even without an identity provider: it keeps your MG cloud password out of the browser login form.

```bash
AUTH_USERNAME=jos
AUTH_PASSWORD=<a long random password>
```

If no sign-in method is left standing -- no OIDC, and no credentials or an explicit `AUTH_PASSWORD_LOGIN_ENABLED=false` -- the API refuses to start rather than coming up with no way in.

### Running both at once

Configuring OIDC normally turns the password login off. `AUTH_PASSWORD_LOGIN_ENABLED` overrides that in either direction:

| Value | Effect |
| --- | --- |
| _unset_ | Password login is on only while no OIDC provider is configured (the default) |
| `true` | Password login stays available alongside single sign-on |
| `false` | Password login is off, whether or not OIDC is configured |

```bash
OIDC_AUTHORITY=https://auth.example.com/application/o/garagestack/
OIDC_CLIENT_ID=garagestack
OIDC_CLIENT_SECRET=...
AUTH_PASSWORD_LOGIN_ENABLED=true
AUTH_USERNAME=breakglass
AUTH_PASSWORD=<a long random password>
```

The login page then shows the "Sign in with ..." button, an "or" divider, and the username/password form. Auto-login still applies: with `OIDC_AUTO_LOGIN=true` you are sent to the provider immediately, so reach the form through the logout link or by opening `/login?loggedOut=1` directly.

Worth keeping in mind before switching this on:

- It is a second door your identity provider knows nothing about, so none of its policies apply to it: no MFA, no account lockout, no central revocation. Give it a long, dedicated password rather than reusing the MG account.
- It stays reachable for anyone who can reach GarageStack, which is exactly why it is useful as break-glass access, and exactly why it is off by default.
- The API logs a warning at every start while both are enabled.

The usual reason to want it: your identity provider runs on the same machine as GarageStack, and you would rather not lose access to the car when that container is down.

---

## Sessions and cookies

| | |
| --- | --- |
| Cookie | `garagestack-auth`, HTTP-only, `SameSite=Strict`, path `/` |
| Contents | Encrypted with the ASP.NET Data Protection keys in the `api_dataprotection` volume (`/data/dataprotection` for all-in-one). Losing them logs everyone out; nothing else breaks. |
| Lifetime | `AUTH_SESSION_LIFETIME_HOURS` (default 7 days) for OIDC; 12 hours or 30 days for the password login |
| Logout | Deletes the cookie **and** records the session as revoked in the database, so a copied cookie is refused on its next request |

Set `AUTH_COOKIE_SECURE=true` whenever GarageStack is reachable over HTTPS. It defaults to `false` so plain-HTTP LAN installs work out of the box.

---

## Upgrading from the previous login

Before this change, the login form checked your MG cloud credentials and issued a JWT signed with `JWT_SECRET`.

- **`JWT_SECRET` is no longer used.** Leaving it in `.env` does no harm; you can delete it. Sessions are now ASP.NET Core cookie tickets, encrypted with the Data Protection keys that already live in the `api_dataprotection` volume, so there is nothing left for that secret to sign. The login itself is unchanged.
- **Everyone is signed out once** when you upgrade. Old JWT cookies are not readable by the new session format.
- **Nothing else changes if you do not configure OIDC.** The password login keeps working with the same credentials and the same 12 hour / 30 day "remember me" behaviour.

---

## Troubleshooting

### "Invalid redirect URI" / "redirect_uri did not match" at the provider

GarageStack derives the redirect URI from the incoming request. Behind a reverse proxy that does not forward the original host and scheme, it can get this wrong. Check the URI the provider logs, then either fix the proxy headers (`Host`, `X-Forwarded-Proto`) or set it explicitly:

```bash
OIDC_REDIRECT_URI=https://garage.example.com/api/auth/oidc/callback
```

The bundled nginx already forwards the right headers, including the port.

### Back at the login page with "Single sign-on failed", and the API log shows `invalid_client`

The provider refused GarageStack's own credentials, before any user was involved. Two causes:

- **The client authentication method.** ASP.NET Core sends `client_id`/`client_secret` in the request body (`client_secret_post`). Providers that expect HTTP Basic (`client_secret_basic`) and enforce it, Authelia among them, answer `invalid_client`. Set the client to `client_secret_post`.
- **The secret itself.** Providers that store a hash (again, Authelia) need the hash in their config and the plaintext in `OIDC_CLIENT_SECRET`. Pasting the hash into GarageStack fails exactly this way.

The provider's own log names which of the two it was. If the stack trace mentions `PushAuthorizationRequest`, it failed at the pushed-authorization endpoint, which is simply the first call the handler makes; the cause is the same.

### Back at the login page with "Single sign-on failed"

The callback did not validate. The API log has the reason. Common causes: the correlation cookie expired (the sign-in took longer than 15 minutes), the server clock is off by more than a few minutes, or `AUTH_COOKIE_SECURE=true` while GarageStack is served over plain HTTP, which makes the browser drop the cookies.

### "This account is not allowed to use GarageStack"

Authentication succeeded, authorization did not. The log line names the account and the groups that were seen. For `OIDC_ALLOWED_EMAILS` it says whether the provider sent an email claim at all, but it never logs the address itself; compare the account's email at the provider with the allow-list. If the provider sends no groups at all, either add a group claim (see your provider above) or switch to `OIDC_ALLOWED_EMAILS`.

### The provider is served over plain HTTP

```bash
OIDC_REQUIRE_HTTPS_METADATA=false
```

Only sensible when the provider is on your own LAN.

### `/api/auth/oidc/login` returns 404

OIDC is not enabled: `OIDC_AUTHORITY` is empty or was not passed into the container. Check the startup log line under `Authentication:`.

### The API refuses to start with "No authentication method is configured"

Neither OIDC nor `AUTH_USERNAME`/`AUTH_PASSWORD` (nor `SAIC_USER`/`SAIC_PASSWORD`) are set. The sibling message, "No authentication method is available", means credentials do exist but `AUTH_PASSWORD_LOGIN_ENABLED=false` switched off the only way in.

### The login page keeps showing the old username/password form after configuring OIDC

The frontend is a separate image from the API, and its bundle is cached by the service worker. Rebuild both (`docker compose build --no-cache api frontend`), then load the page in a private window. `curl -s <your URL>/api/auth/config` tells you which side is stale: `{"oidcEnabled":true,...}` means the API is fine and the browser or the frontend image is not.

---

## API endpoints

| Endpoint | Purpose |
| --- | --- |
| `GET /api/auth/config` | Which sign-in methods are enabled. Public: the login page needs it before anyone is signed in. |
| `GET /api/auth/oidc/login?returnUrl=/map` | Starts the flow. Redirects to the provider; `returnUrl` must be a local path. |
| `GET /api/auth/oidc/callback` | Where the provider sends the user back. Handled by ASP.NET Core. |
| `GET /api/auth/me` | Current user and session expiry. |
| `POST /api/auth/login` | Built-in password login. Returns 404 while it is disabled, which is the default once OIDC is configured. |
| `POST /api/auth/logout` | Ends and revokes the session. |
