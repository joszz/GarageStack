# Contributing to GarageStack

Thank you for your interest in contributing! This document explains how to get involved.

See [ARCHITECTURE.md](ARCHITECTURE.md) for how the services (Worker, Api, Mosquitto, Postgres, frontend) fit together before diving into a change that spans more than one of them.

## Getting Started

Don't have a real MG vehicle or SAIC account to test against? See [DEMO.md](DEMO.md) -- demo mode runs the full app against realistic fake data with no MG credentials, database, or MQTT broker required, and is the fastest way to get the frontend running locally.

To work against the full stack (real or self-provided credentials):

1. Fork the repository and clone it locally.
2. Install prerequisites: .NET (latest LTS), Node.js, PNPM, PostgreSQL, Docker (optional).
3. Copy `.env.example` to `.env` and fill in your values.
4. Run `pnpm install` inside the `frontend/` directory.
5. Run `dotnet restore` from the repository root (the solution spans multiple projects under `src/`).

## Development Workflow

- Create a new branch from `main` for your change: `git checkout -b feature/my-change`.
- Keep changes focused -- one feature or fix per pull request.
- Run the test suite before opening a PR:
  - Backend: `dotnet test`
  - Frontend: `pnpm test:unit`
- Make sure linting passes: `pnpm lint`

## Commit Style

Use short, imperative commit messages: `Add trip heatmap filter`, `Fix battery percentage rounding`.

## Pull Requests

- Fill in the PR template fully.
- Link any related issues with `Closes #<number>`.
- PRs require at least one approving review before merge.

## Reporting Issues

Use the issue templates provided in the repository. Include reproduction steps, environment details, and relevant logs.

## License

By contributing you agree that your contributions will be licensed under the same [MIT License](LICENSE) that covers the project.
