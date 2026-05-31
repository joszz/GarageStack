# Security Policy

## Supported Versions

Only the latest release is actively maintained and receives security fixes.

| Version | Supported |
|---------|-----------|
| latest  | yes       |
| older   | no        |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Use [GitHub's private vulnerability reporting](../../security/advisories/new) to submit a report. Include:

- A description of the vulnerability
- Steps to reproduce
- Potential impact
- Any suggested fix (optional)

Responses are on a best-effort basis. You will receive an acknowledgment once the report has been reviewed, and a follow-up when the issue is resolved or dismissed.

## Scope

This project interacts with the MG/SAIC iSmart API and stores vehicle data. Areas of particular concern:

- Authentication and session handling
- API credential storage and transmission
- Personally identifiable information (PII) and location data
- Redis and PostgreSQL access controls
- PWA push notification endpoints

## Disclosure Policy

Once a fix is available, a coordinated disclosure timeline will be agreed upon with the reporter. Credit will be given in the release notes unless anonymity is requested.

## Security Practices

- API rate limiting is enforced server-side
- All secrets are managed via environment variables (never committed)
- Client-side security headers are applied
- Dependencies are kept up to date
