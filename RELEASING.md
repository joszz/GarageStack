# Releasing GarageStack

---

## Deployment Options

GarageStack ships two ways. Users pick one.

| Option | Best for | Images used |
|--------|----------|-------------|
| **All-in-one** | Unraid, NAS, single-machine homelab | `garagestack` |
| **Separate services** | Docker Compose on a server, more control | `garagestack-frontend`, `garagestack-api`, `garagestack-worker` + external postgres + mosquitto |

---

## Published Images

All images live on GitHub Container Registry (`ghcr.io`). Replace `joszz` with the actual username.

| Image | Purpose |
|-------|---------|
| `ghcr.io/joszz/garagestack` | All-in-one (Unraid / single container) |
| `ghcr.io/joszz/garagestack-frontend` | nginx serving the Vue SPA |
| `ghcr.io/joszz/garagestack-api` | ASP.NET Core REST API |
| `ghcr.io/joszz/garagestack-worker` | .NET background worker (MQTT + push) |

### Tags Produced Per Image

| Tag | Example | When created |
|-----|---------|--------------|
| `latest` | `garagestack:latest` | Every push to `main`, weekly rebuild (Mondays 05:00 UTC), manual dispatch |
| `1.2.3` | `garagestack:1.2.3` | On `v1.2.3` git tag |
| `1.2` | `garagestack:1.2` | On any `v1.2.x` tag |
| `1` | `garagestack:1` | On any `v1.x.x` tag |
| `sha-abc1234` | `garagestack:sha-abc1234` | Every push to `main`, weekly rebuild, manual dispatch |

---

## Automated Release Pipeline

### Overview

```
PR opened
  CI (lint, typecheck, test, build, container-build)
  CodeQL (actions, C#, JS/TS analysis)
  Security (Trivy filesystem scan)

Merge to main
  CI
  CodeQL
  Security (Trivy filesystem + image scan after publish)
  Dependency submission (graph snapshot to GitHub)
  Release Please (update release PR)
  docker-publish (push images with short SHA + latest tags)
    Sign with Cosign
    Attach SBOM + provenance

Release PR merged
  Release Please (create GitHub Release + vX.Y.Z tag)
  docker-publish (push versioned tags + latest)
    Sign with Cosign
    Attach SBOM + provenance

Weekly (Mondays)
  CodeQL
  Security (Trivy filesystem + image scan)
  docker-publish (rebuild and re-push latest tags)
```

---

## Release Process

### Commit Convention

All commits to `main` must follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<optional scope>): <description>
```

| Type | Version effect | Example |
|------|---------------|---------|
| `feat` | Minor bump | `feat: add trip heatmap export` |
| `fix` | Patch bump | `fix: correct kWh calculation` |
| `feat!` or `BREAKING CHANGE` footer | Major bump | `feat!: rename API routes` |
| `chore`, `ci`, `docs`, `test`, `build` | No bump | `chore: update dependencies` |

### Automated Release PRs (Release Please)

[Release Please](https://github.com/googleapis/release-please) monitors commits on `main`. When releasable commits accumulate, it:

1. Opens or updates a **Release PR** containing an updated `CHANGELOG.md` and version bump
2. The PR is kept up-to-date as new commits land on `main`

**To trigger a release: merge the Release PR.** Release Please will create a GitHub Release and tag the commit as `vX.Y.Z`. The `docker-publish` workflow fires automatically on the tag.

### Manual Tagging (fallback)

If Release Please is not yet active or you need an out-of-band release:

```bash
git checkout main && git pull origin main
git tag v1.2.3
git push origin v1.2.3
```

Watch progress at `https://github.com/joszz/garagestack/actions`.

---

## CI and Quality Gates

The `ci` workflow runs on every PR and push to `main`. All jobs must pass before a PR can be merged.

| Job | What it checks |
|-----|---------------|
| `frontend-lint` | oxlint + ESLint (no fixable issues left uncommitted), Prettier format |
| `frontend-typecheck` | `vue-tsc --build` (zero type errors) |
| `frontend-test` | Vitest unit tests |
| `backend-build` | `dotnet build --configuration Release` |
| `backend-test` | `dotnet test` (all xUnit tests) |
| `container-build` | Docker build (no push) for all four images |

### Code Analysis (CodeQL)

The `codeql` workflow runs alongside CI on every PR, every push to `main`, and weekly on Mondays at 07:00 UTC. It analyzes three language targets: `actions` (workflow files), `csharp` (backend), and `javascript-typescript` (frontend). Results appear in the GitHub Security tab.

---

## Dependency Management

[Dependabot](https://docs.github.com/en/code-security/dependabot) opens weekly PRs on Mondays for outdated packages across npm (frontend), NuGet (backend), and GitHub Actions.

### Auto-merge Rules

The `auto-merge` workflow merges Dependabot PRs automatically when CI passes, covering:

- **All patch updates** (any package, any ecosystem, including GitHub Actions)
- **Minor updates** for any named Dependabot group (the condition is `dependency-group != ''`) -- this covers GitHub Actions too, via the `docker-actions`/`github-actions` groups below

Named groups defined in `.github/dependabot.yml`:

| Ecosystem | Groups |
| --------- | ------ |
| npm | `vue-ecosystem`, `vite-and-pwa`, `map-and-charts`, `fontawesome`, `linting`, `typescript-and-types`, `testing` |
| NuGet | `dotnet-platform`, `identity-model`, `serilog`, `test-framework` |
| GitHub Actions | `docker-actions`, `github-actions` |

Everything else (ungrouped minor and all major updates) requires manual review and merge.

---

## Security Scanning

The `security` workflow runs Trivy on every PR, every push to `main`, and weekly on Mondays at 07:00 UTC.

### Filesystem Scan

Scans source code and dependency manifests for known CVEs:

- Reports `CRITICAL` and `HIGH` severity findings as SARIF, visible under the GitHub Security tab
- **Fails the build** on any unfixed `CRITICAL` vulnerability

### Image Scan

After images are pushed to GHCR, each `:latest` image is scanned separately. Results appear in the Security tab under per-image categories.

---

## Supply Chain Security

### SBOM

Every published image includes a Software Bill of Materials generated by BuildKit and attached as an OCI attestation alongside the manifest.

Inspect the SBOM:

```bash
docker buildx imagetools inspect ghcr.io/joszz/garagestack:latest \
  --format '{{ json .SBOM }}'
```

### Provenance Attestation

Each image includes a full SLSA provenance attestation (`mode=max`) recording the exact build inputs, environment, and steps.

Inspect provenance:

```bash
docker buildx imagetools inspect ghcr.io/joszz/garagestack:latest \
  --format '{{ json .Provenance }}'
```

### Cosign Image Signing

Every pushed image is signed with [Cosign](https://docs.sigstore.dev/cosign/overview/) using keyless signing via GitHub Actions OIDC. No long-lived signing keys are stored anywhere.

Verify a signature:

```bash
cosign verify \
  --certificate-identity-regexp "https://github.com/joszz/garagestack/.*" \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
  ghcr.io/joszz/garagestack:latest
```

A successful verification confirms the image was built by this repository's workflow and was not tampered with after publishing.

### Dependency Submission

The `dependency-submission` workflow runs on every push to `main`. It uses the `component-detection-dependency-submission-action` to snapshot all dependency graphs (npm, NuGet, GitHub Actions) and submit them to GitHub. This powers the Dependency graph and Dependabot alerts in the repository Security tab.

---

## Stale Issues and PRs

The `stale` workflow runs every Monday at 08:00 UTC:

- Issues and PRs inactive for **60 days** are labelled `stale`
- Issues and PRs still inactive after a further **14 days** are closed
- Exempt labels for issues: `pinned`, `security`, `bug`, `enhancement`
- Exempt labels for PRs: `pinned`, `security`, `do-not-merge`
- Issues and PRs attached to a milestone are always exempt

---

## Versioning (MinVer)

GarageStack uses MinVer for .NET assembly version metadata.

- Source of truth is git history + tags with `v` prefix (e.g. `v1.2.3`)
- On non-tag commits, MinVer produces prerelease versions using `preview.0` as the identifier
- If no `v*` tags exist yet, MinVer starts from `0.0.0-preview.0.<height>`
- In CI, the workflow computes MinVer once and passes it to all Docker builds as `APP_VERSION`

**Do not manually set `<Version>` in `*.csproj` for releases.** MinVer reads it from git.

Use `0.x.y` while in active development, `1.0.0` for the first public release.

---

## Required Secrets and Permissions

| Secret / Permission | Where | Purpose |
|--------------------|-------|---------|
| `GITHUB_TOKEN` | Auto-provided | GHCR login, PR merge, release creation |
| `GHCR_PAT` | Optional repo secret | Override GITHUB_TOKEN for GHCR push |
| `DOCKERHUB_USERNAME` | Repo secret | Avoid Docker Hub pull rate limits during builds |
| `DOCKERHUB_TOKEN` | Repo secret | Avoid Docker Hub pull rate limits during builds |
| Actions: Read and write permissions | Repo settings | Required for GHCR push and release creation |
| `id-token: write` | Workflow-level | Cosign keyless signing via OIDC |

---

## Making GHCR Packages Public

After the very first successful workflow run, each package is created as private. Repeat once per image (four times total):

1. GitHub profile -> **Packages**
2. Select the package
3. **Package settings** -> **Change visibility** -> **Public**

---

## Using Published Images with Docker Compose

The default `docker-compose.yml` builds from local source. To use published images, replace the `build:` block with an `image:` reference:

```yaml
api:
  image: ghcr.io/joszz/garagestack-api:1.2.3
  # build: ...  <-- remove
```

Repeat for `frontend` and `worker`. Leave `mosquitto` and `postgres` services unchanged.

---

## Unraid Community Apps

The Unraid template lives at [unraid/garagestack.xml](unraid/garagestack.xml). It tracks the `:latest` tag so Unraid users receive updates automatically -- no template change is needed when cutting a new release.

### One-time submission to Community Apps

The CA system requires a **separate dedicated repository** based on the [unraid-community-apps-starter](https://github.com/unraid/unraid-community-apps-starter) template. The main GarageStack repo is not read directly.

**Steps:**

1. Ensure the GitHub repo is **public** and the MIT `LICENSE` file is committed
2. Ensure all four GHCR packages are **public** (see [Making GHCR Packages Public](#making-ghcr-packages-public))
3. Go to [github.com/unraid/unraid-community-apps-starter](https://github.com/unraid/unraid-community-apps-starter) and click **Use this template** to create a new repo (e.g. `joszz/unraid-community-apps`)
4. In the new repo, fill in `ca_profile.xml` (description, icon, project link, support link)
5. Copy `unraid/garagestack.xml` from this repo into `templates/garagestack.xml` in the new repo
6. Update the `<TemplateURL>` inside that copy to point to its new raw URL:
   `https://raw.githubusercontent.com/joszz/unraid-community-apps/main/templates/garagestack.xml`
7. Go to [ca.unraid.net/submit/new](https://ca.unraid.net/submit/new) and submit the new repo URL
8. Once approved, GarageStack appears in the Community Apps search

### Manual install (before CA approval or for testing)

Unraid users can install directly without waiting for CA approval:

> Unraid UI -> Apps -> Install from URL -> `https://raw.githubusercontent.com/joszz/garagestack/main/unraid/garagestack.xml`

### Per-release checklist

The template uses `:latest` so no file changes are needed for normal releases. Only update `unraid/garagestack.xml` (and mirror the change to `templates/garagestack.xml` in the CA repo) if:

- A new required environment variable is added
- A port or volume mapping changes
- The container name or description changes

If the template changes, commit it alongside the release so users who refresh their template get the updated fields.

---

## Rollback

If a release is broken, tag the last good commit with a new patch version:

```bash
git tag v1.2.4 <good-commit-sha>
git push origin v1.2.4
```

Find the previous release SHA:

```bash
git log --oneline --tags
```

---

## Troubleshooting GHCR Denied Errors

If workflow logs show `denied: denied` during push, common causes:

1. **Workflow permissions** not set to **Read and write** in Repository Settings -> Actions -> General.
2. Package-level access does not allow this repository to publish to the GHCR package.
3. Org policy blocks the default workflow token -- create a `GHCR_PAT` secret with `read:packages` and `write:packages` scopes.

The workflow runs a GHCR auth preflight per image before the expensive build steps and fails early with a clear permission message.
