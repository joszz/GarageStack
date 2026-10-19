# Contributing to GarageStack

Thank you for your interest in contributing! This document explains how to get involved.

See [ARCHITECTURE.md](../documentation/ARCHITECTURE.md) for how the services (Worker, Api, Mosquitto, Postgres, frontend) fit together before diving into a change that spans more than one of them.

## Getting Started

Don't have a real MG vehicle or SAIC account to test against? See [DEMO.md](../documentation/DEMO.md) -- demo mode runs the full app against realistic fake data with no MG credentials, database, or MQTT broker required, and is the fastest way to get the frontend running locally.

To work against the full stack (real or self-provided credentials):

1. Fork the repository and clone it locally.
2. Install prerequisites: .NET (latest LTS), Node.js 26 (what CI and the images build with), PNPM, PostgreSQL, Docker (optional). Use the PNPM version in the `packageManager` field of `frontend/package.json`: `pnpm self-update <version>` installs it. Node 25 and later no longer bundle corepack, so `corepack enable` only works after `npm install --global corepack`. PNPM does not switch to it by itself in this repo (see the note in `frontend/pnpm-workspace.yaml`).
3. Copy `.env.example` to `.env` and fill in your values.
4. Run `pnpm install` inside the `frontend/` directory.
5. Run `dotnet restore` from the repository root (the solution spans multiple projects under `src/`).

## Development Workflow

- Create a new branch from `main` for your change: `git checkout -b feature/my-change`.
- Keep changes focused -- one feature or fix per pull request.
- Run the test suite before opening a PR:
  - Backend: `dotnet test --project src/GarageStack.Tests`
  - Frontend: `pnpm test:unit`
- The backend tests in `src/GarageStack.Tests/Postgres` run against a real PostgreSQL, for what the in-memory database cannot show (indexes, concurrency tokens, bulk updates, the SQL a query becomes). Without one they are skipped; CI always runs them. To run them, start a server and say where it is:

  ```bash
  docker run -d --name garagestack-test-db -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:18-alpine
  GARAGESTACK_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres" dotnet test --project src/GarageStack.Tests
  ```

  Each test gets a database of its own, copied from one the migrations build at the start of the run, and drops it afterwards.
- Coverage, which CI reports on every run:
  - Backend: add `--coverage --coverage-output-format cobertura --coverage-settings src/GarageStack.Tests/coverage.settings.xml` to the test command; the report lands in `TestResults/`.
  - Frontend: `pnpm test:coverage`, with an HTML report in `frontend/coverage/`.
- Touching the frontend, nginx config or the Content-Security-Policy? Run the browser smoke tests too. They drive the production bundle behind the production nginx config, which is the only place the policy applies:

  ```bash
  cp .env.demo.example .env.demo
  docker compose --env-file .env.demo -f docker-compose.demo.yml up -d --build   # repository root
  cd frontend && pnpm exec playwright install chromium && pnpm test:e2e
  docker compose --env-file .env.demo -f docker-compose.demo.yml down             # when you are done
  ```

- Make sure linting and formatting pass:
  - Backend: `dotnet build` (analyzers run as part of the build, warnings are errors) and `dotnet format GarageStack.slnx`
  - Frontend: `pnpm lint` (oxlint, ESLint and Stylelint, each with `--fix`) and `pnpm format`
- Changed an EF Core entity? Add a migration from the repository root (no Api configuration needed). `dotnet tool restore` installs the pinned `dotnet-ef` first:
  `dotnet ef migrations add <Name> --project src/GarageStack.Data --startup-project src/GarageStack.Data`

## Adding a language

GarageStack ships in English and Dutch. A new language touches these places (use its two-letter code, here `de`):

- **Interface:** copy `frontend/src/locales/en.json` to `de.json` and translate the values; keys and `{placeholders}` stay as they are. English is the fallback for any key a language lacks.
- **Language list:** add `'de'` to the `Locale` type in `frontend/src/stores/settingsShared.ts` (and to its browser-language guess just below it), a loader to `loadMessages` in `frontend/src/i18n.ts` (each language is a chunk fetched when first chosen), and its number and date locale in `frontend/src/utils/format.ts`.
- **Language setting:** Settings > Language is a two-way switch with flags (`frontend/src/components/settings/SettingsModal.vue`). A third language turns it into a choice of several.
- **Notifications:** copy `src/GarageStack.Worker/Resources/NotificationStrings.resx` to `NotificationStrings.de.resx`. `NOTIFICATION_LANGUAGE=de` then picks it; document the value in `documentation/CONFIGURATION.md`.
- **Homepage widget and API:** copy `src/GarageStack.Api/Resources/WidgetStrings.resx` to `WidgetStrings.de.resx`, and add the culture to `SupportedCultures` and `SupportedUICultures` in `src/GarageStack.Api/Program.cs`.
- **Place names:** add the code to `GeocodeDefaults.SupportedLanguages` in `src/GarageStack.Core/Configuration` so addresses come back in that language.

`en.json` and `nl.json` hold the same keys; keep that true for the new file, since a missing key falls back to English silently.

## Commit Style

Commits follow [Conventional Commits](https://www.conventionalcommits.org/), with a short imperative description after the type: `feat: add trip heatmap filter`, `fix(frontend): round the battery percentage`. The type decides which heading of the release notes a change goes under; see [RELEASING.md](../documentation/RELEASING.md#commit-convention).

## Pull Requests

- Fill in the PR template fully.
- Link any related issues with `Closes #<number>`.
- PRs require at least one approving review before merge.

## CI on a fork

The Docker build workflow requires two repository secrets to avoid Docker Hub anonymous pull rate limits (GitHub runners share IPs and exhaust the limit quickly):

- **`DOCKERHUB_USERNAME`** - Your Docker Hub username
- **`DOCKERHUB_TOKEN`** - A Docker Hub access token (hub.docker.com > Account Settings > Security > New Access Token)

Add them under **Settings > Secrets and variables > Actions** in your fork. A free Docker Hub account is sufficient.

## Reporting Issues

Use the issue templates provided in the repository. Include reproduction steps, environment details, and relevant logs.

## License

By contributing you agree that your contributions will be licensed under the same [MIT License](../LICENSE) that covers the project.
