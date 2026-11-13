# Instructions for Copilot

GarageStack is a self-hosted dashboard for MG cars: a .NET backend (Api and Worker), a Vue 3 and TypeScript frontend, PostgreSQL, and the SAIC MQTT gateway. [`documentation/ARCHITECTURE.md`](../documentation/ARCHITECTURE.md) describes how the parts fit together.

## Rules that are settled

- **CSS**: plain CSS in `frontend/src/assets/*.css`, built on the custom properties (tokens) defined there. No Bootstrap, no Tailwind, no Sass or other preprocessor. Reuse the existing classes (`btn`, `card`, `text-muted`, ...) before adding new ones.
- **No inline styles**: the Content Security Policy forbids them. No `style` attributes and no `<style>` blocks in components.
- **TypeScript only** in the frontend, with no `any`, no `@ts-ignore` and no lint suppressions.
- **pnpm**, never npm or yarn.
- **.NET stays on the latest LTS** (10 today). Do not move packages, the SDK or the base images to a non-LTS major.
- **Translations**: every text a user sees goes through vue-i18n. A new key goes into `en.json`, `nl.json` and `es.json` alike, or the type check fails.
- **The current time** comes from an injected `TimeProvider`, never `DateTime.UtcNow` or `DateTimeOffset.UtcNow`: `BannedSymbols.txt` fails the build on those outside the tests, entity defaults and demo data.
- **Dates, times and units** are formatted through `utils/format.ts` and `useUnits()`, never by hand.
- **Icons** are Font Awesome, **charts** are Chart.js.
- **Responsive**: every screen has to work from 320 px wide.
- **No em dashes** anywhere, in code, comments or documentation.
- **Test data**: never a real VIN or licence plate. Use synthetic ones such as `FAKEVN00000000001`.

## Before finishing

- Add or update unit tests for what changed, backend and frontend.
- Backend: `dotnet test --project src/GarageStack.Tests` and `dotnet format GarageStack.slnx --verify-no-changes`.
- Frontend, inside `frontend/`: `pnpm test:unit --run`, `pnpm type-check`, `pnpm lint`, and `pnpm exec prettier --write` on every file touched.
