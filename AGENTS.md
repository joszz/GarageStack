# Working on GarageStack

For coding agents and people alike. GarageStack is a self-hosted dashboard for MG cars: a .NET backend (Api and Worker), a Vue 3 and TypeScript frontend, PostgreSQL, and the SAIC MQTT gateway.

## Where to read first

- [`documentation/ARCHITECTURE.md`](documentation/ARCHITECTURE.md): how the parts fit together.
- [`documentation/openapi.json`](documentation/openapi.json): every API endpoint, what it takes and answers, and its refusal codes. Quicker than reading the endpoint files. [`documentation/API.md`](documentation/API.md) explains it.
- [`.github/CONTRIBUTING.md`](.github/CONTRIBUTING.md): setting up, and the tests.

## Seeing a change working

Run demo mode: the whole app on sample data, without a car, a database or a broker. [`documentation/DEMO.md`](documentation/DEMO.md#from-a-script-or-an-ai-agent) says how to start it in the background, sign in from a script, drive the demo car, and what to do when a developer's own demo already runs on the same ports. Do not stop processes you did not start.

## Rules that are settled

- **CSS**: plain CSS in `frontend/src/assets/*.css`, built on the custom properties (tokens) defined there. No Bootstrap, no Tailwind, no Sass or other preprocessor. Reuse the existing classes (`btn`, `card`, `text-muted`, ...) before adding new ones.
- **No inline styles**: the Content Security Policy forbids them. No `style` attributes and no `<style>` blocks in components.
- **TypeScript only** in the frontend, with no `any`, no `@ts-ignore` and no lint suppressions.
- **pnpm**, never npm or yarn.
- **.NET follows the newest major** (11 today). A new major moves by hand, all at once (target framework, packages, SDK, images, CI), once the Npgsql EF provider for it is out.
- **Translations**: every text a user sees goes through vue-i18n. A new key goes into every file in `frontend/src/locales/` (`en`, `nl`, `es`, `pt`), or the type check fails.
- **The current time** comes from an injected `TimeProvider`, never `DateTime.UtcNow` or `DateTimeOffset.UtcNow`: `BannedSymbols.txt` fails the build on those outside the tests, entity defaults and demo data.
- **Dates, times and units** are formatted through `utils/format.ts` and `useUnits()`, never by hand.
- **Icons** are Font Awesome, **charts** are Chart.js.
- **Responsive**: every screen has to work from 320 px wide.
- **No em dashes** anywhere, in code, comments or documentation.
- **Test data**: never a real VIN or licence plate. Use synthetic ones such as `FAKEVN00000000001`.
- **Demo mode keeps up**: a new field or feature gets demo data in `src/GarageStack.Data/Demo/`, or it never shows without a car.
- **Endpoints describe themselves**: a summary, what they answer, a described tag. See [`documentation/API.md`](documentation/API.md#changing-the-api).
- **Deployment templates**: `unraid/garagestack.xml` and `zimaos/docker-compose.yml` are deployment templates. A new required variable, or a changed list of values (such as the regions), goes into both.
- **Line endings**: keep each file's own. A scripted edit that turns a CRLF file into LF fails `dotnet format --verify-no-changes`.

## Before finishing

- Add or update unit tests for what changed, backend and frontend.
- A change to trips (anything on `ITripRepository`) is made in both `TripRepository` and `DemoTripRepository`, with shared logic in a Core helper, and gets a step in `TripRepositoryContractTests`, which fails until every repository method is covered there or exempt with a reason.
- Backend: `dotnet test --project src/GarageStack.Tests` and `dotnet format GarageStack.slnx --verify-no-changes`. A change to the API rewrites `documentation/openapi.json` on the first test run; commit it with the change.
- Frontend, inside `frontend/`: `pnpm test:unit --run`, `pnpm type-check`, `pnpm lint`, and `pnpm exec prettier --write` on every file touched.
- Touched nginx, the Content-Security-Policy or a page's loading? Run the browser tests in `frontend/e2e` against the demo Docker stack (see CONTRIBUTING).
