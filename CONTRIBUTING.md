# Contributing to GarageStack

Thank you for your interest in contributing! This document explains how to get involved.

## Getting Started

1. Fork the repository and clone it locally.
2. Install prerequisites: .NET (latest LTS), Node.js, PNPM, PostgreSQL, Redis, Docker (optional).
3. Copy `.env.example` to `.env` and fill in your values.
4. Run `pnpm install` inside the `frontend/` directory.
5. Run `dotnet restore` in the backend directory.

## Development Workflow

- Create a new branch from `main` for your change: `git checkout -b feature/my-change`.
- Keep changes focused -- one feature or fix per pull request.
- Run the test suite before opening a PR:
  - Backend: `dotnet test`
  - Frontend: `pnpm test`
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
