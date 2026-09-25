# Security Policy

## Supported Versions

Only the latest release is actively maintained and receives security fixes.

| Version | Supported |
|---------|-----------|
| latest  | yes       |
| older   | no        |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Use [GitHub's private vulnerability reporting](../../../security/advisories/new) to submit a report. Include:

- A description of the vulnerability
- Steps to reproduce
- Potential impact
- Affected component (backend API, frontend, database layer, push notifications, etc.)
- Any suggested fix (optional)

### Response Timeline

Responses are on a best-effort basis. You will receive an acknowledgment once the report has been reviewed, and a follow-up when the issue is resolved or dismissed. If you have not heard back after a reasonable period, please follow up on the same advisory thread.

## Bug Bounty

This is a FOSS project maintained on a volunteer basis. There is no bug bounty program. Recognition in release notes is offered in lieu of monetary reward (unless anonymity is requested).

## Scope

This project interacts with the MG/SAIC iSmart API and stores vehicle data. Areas of particular concern:

- Authentication and session handling
- API credential storage and transmission
- Personally identifiable information (PII) and location/route data
- PostgreSQL access controls
- PWA push notification endpoints and VAPID key handling
- Third-party dependency vulnerabilities with direct exploit paths

### Out of Scope

The following are generally not considered in scope:

- Vulnerabilities in the upstream [SAIC-iSmart-API](https://github.com/SAIC-iSmart-API) -- report those upstream
- Self-XSS or attacks requiring physical access to the victim's device
- Denial-of-service attacks
- Social engineering
- Missing security headers that have no practical exploit path in this context
- Findings from automated scanners without a demonstrated impact

## Disclosure Policy

Once a fix is available, a coordinated disclosure timeline will be agreed upon with the reporter. The default embargo is 90 days from the fix being merged. Credit will be given in the release notes unless anonymity is requested.

## Safe Harbor

This project follows a coordinated vulnerability disclosure model. Good-faith security research that follows responsible disclosure practices and avoids data destruction, unauthorized access to others' accounts, or service disruption will not be pursued legally.

## Security Practices

### Secrets and Credentials

- All secrets (API keys, database credentials, VAPID keys) are managed via environment variables and never committed to the repository
- `.env` files are listed in `.gitignore`; example files use placeholder values only

### API and Network

- API rate limiting is enforced server-side, per client IP, with tighter limits on the login and widget endpoints
- Sessions are encrypted, HTTP-only, `SameSite=Strict` cookies, revoked server-side on logout
- Plain HTTP on a LAN is the documented default. Put a TLS-terminating proxy in front for anything reachable from outside the LAN and set `AUTH_COOKIE_SECURE=true` so the session cookie is only sent over HTTPS
- CORS is restricted to the configured origin, and state-changing requests carrying an `Origin` header are rejected unless it matches

### Frontend

- Content Security Policy (CSP) headers are applied to every response, restricting scripts/styles to same-origin plus specific sha256 hashes (`script-src 'self' 'sha256-...'`) -- there are no third-party CDN assets to apply SRI to; everything is self-hosted and bundled at build time
- `localStorage` holds only UI preferences and the signed-in display name with its session expiry; the session itself lives in the HTTP-only cookie

### Database

- The bundled deployments use one PostgreSQL role, owned by the application, for both the API and the Worker. There is no split into per-service roles; on an external server you can create a dedicated role with access to the `garagestack` database only
- Personal identifiers are kept out of the rotating log files: the MG account email and VIN are redacted from MQTT topics before logging, VINs are shortened to their last four characters, and failed login attempts log only the client IP. Location data is stored in the database and never logged

### Dependencies

- Dependencies are kept up to date; known vulnerabilities are addressed promptly
- Dependabot or equivalent tooling is used to surface outdated or vulnerable packages

### Push Notifications

- VAPID keys are generated per deployment and stored as environment variables
- Push endpoints are validated server-side before use
