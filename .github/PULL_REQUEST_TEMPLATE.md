# Pull Request

## Summary

Describe what this PR does and why.

## Type of Change

- [ ] Bug fix
- [ ] New feature
- [ ] Refactor / cleanup
- [ ] Documentation
- [ ] Dependency update
- [ ] Other

## Related Issues

Closes #

## Checklist

- [ ] Tests added or updated
- [ ] Checks pass: `pnpm check` in `frontend/`, and `dotnet test --project src/GarageStack.Tests` plus `dotnet format GarageStack.slnx --verify-no-changes` at the root
- [ ] Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat: ...`, `fix(frontend): ...`)
- [ ] If trips changed (`ITripRepository`): done in the demo too, with a step in `TripRepositoryContractTests`
- [ ] If the API changed: the rewritten `documentation/openapi.json` is committed
- [ ] New fields or features have demo data in `src/GarageStack.Data/Demo/`
- [ ] No secrets or personal data committed, and no real VIN or licence plate in test data
- [ ] Responsive on mobile, down to 320 px wide
- [ ] Every new UI text is an i18n key, in every file in `frontend/src/locales/`
- [ ] If `unraid/garagestack.xml` changed: synced to `joszz/unraid-community-apps` after merge, through a pull request there (see "Keeping the Community Apps copy in sync" in `documentation/RELEASING.md`)
