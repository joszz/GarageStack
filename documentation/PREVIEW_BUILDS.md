# Preview builds

A preview build is a set of throwaway images for one pull request, so someone with the right car can try a fix before it is merged. It exists because most of GarageStack's drivetrain code (BEV and PHEV charging, battery counters) cannot be checked on the maintainer's own car: the person who reported the problem is the only one who can say whether the fix works.

- Add the **`test-build`** label to a pull request and every image is published as **`pr-<number>`**.
- Every push to the pull request replaces those images.
- Removing the label, closing the pull request or merging it **deletes the images again**.
- `latest`, the version tags and the `sha-` tags are never touched, so nobody else notices.

Contents:

- [Maintainer checklist](#maintainer-checklist)
- [Testing a preview build](#testing-a-preview-build) (the part to send testers to)
- [Database changes](#database-changes)
- [How it works](#how-it-works)
- [Troubleshooting](#troubleshooting)
- [Before a build: ask for data](#before-a-build-ask-for-data)

---

## Maintainer checklist

1. **Branch and pull request as usual.** Create the branch with `--no-track` from `origin/main` and open a pull request to `main`. While the fix is unproven, refer to the issue with `Refs #123` rather than `Fixes #123`, so a merge does not close the issue before the tester has confirmed it.
2. **Keep migrations out if you can.** See [Database changes](#database-changes).
3. **Add the `test-build` label.** The *Preview build* workflow publishes the four images (a few minutes, since it reuses the build cache of `main`) and then posts a comment on the pull request with the exact image names and commands. That comment is updated on every later push.
4. **Point the tester at it.** Comment on the issue, for example:

   > A build with a possible fix is ready to try. Switch your GarageStack image to `pr-123` as described in [this comment](link to the preview comment), use the car as usual for a day, and let me know what the dashboard shows. Afterwards switch back to `latest`. Step by step: [Testing a preview build](https://github.com/joszz/GarageStack/blob/main/documentation/PREVIEW_BUILDS.md#testing-a-preview-build).

5. **Iterate.** Push more commits to the same branch. The `pr-123` tag follows the pull request, so the tester only has to pull again (Unraid: *Force update*; Compose: run the same command again). The footer version ends in `.pr.123`, and the comment shows which commit it is.
6. **Finish.**
   - **It works:** merge. The images are deleted and the fix reaches everyone through `latest`. Ask the tester to switch back to `latest`.
   - **It does not work:** close the pull request (or just remove the label while you rethink). The images are deleted and nothing else changed.

Need a fresh build without a new commit, for example to pick up a newer `main`? Remove the label and add it again.

---

## Testing a preview build

*This section is written for testers. Link to it from issues.*

A preview build is GarageStack with one change in it that has not been released yet. It runs on your own data like a normal update, and you can go back to the normal version at any time.

**Before you start:** if the preview comment on the pull request carries a warning that the build changes the database, back up your database first (see [Backup and restore](../README.md#backup-and-restore)). Otherwise nothing special is needed.

Replace `pr-123` below with the tag from the pull request comment.

### All-in-one container or Unraid

Change the image from `ghcr.io/joszz/garagestack:latest` to:

```text
ghcr.io/joszz/garagestack:pr-123
```

- **Unraid:** edit the container, change *Repository*, apply.
- **docker run:** stop and remove the container, then run it again with the new image and the same volumes and variables.
- **Compose with the all-in-one image:** change the `image:` line, then `docker compose pull && docker compose up -d`.

When the maintainer says a new version is ready, pull the same tag again (Unraid: *Force update*).

### Docker Compose from a clone of this repository

The repository's `docker-compose.yml` builds GarageStack from source. `docker-compose.preview.yml` swaps the three GarageStack services for the preview images and leaves the database, broker and gateway as they are:

```sh
git pull
PREVIEW_TAG=pr-123 docker compose -f docker-compose.yml -f docker-compose.preview.yml up -d
```

For a newer version of the same preview, run that command again: it always pulls.

On a system where you cannot put a variable in front of the command (PowerShell, the OpenMediaVault compose plugin, Portainer), add `PREVIEW_TAG=pr-123` to your `.env` file instead and add `docker-compose.preview.yml` as an extra compose file. Remove it from `.env` again when you go back.

### Checking that it runs

The version in the footer ends in `.pr.123` (for example `v0.13.2-preview.0.4.pr.123`).

### Going back

- **All-in-one or Unraid:** change the image back to `ghcr.io/joszz/garagestack:latest`.
- **Compose from a clone:** `docker compose up -d --build`, which builds your clone again without the preview file.

Your data stays. Readings collected while the preview ran stay too.

### What to report

On the issue: what you did, what GarageStack showed, and what the MG app showed for the same moment. Screenshots help. **Remove your VIN, location and other personal details first.**

Preview images are deleted when the pull request is closed or merged. If your container then fails to update, that is why: switch back to `latest`.

---

## Database changes

A tester runs the preview against their real database. If the build carries an EF Core migration, it is applied on start, and switching back to `latest` does **not** undo it. Usually the older version still runs on the newer schema, but not always (for example a new non-nullable column without a default).

- Prefer fixes without a migration in a preview. Display, mapping and calculation fixes rarely need one.
- When a migration is unavoidable, the preview comment carries a warning automatically (it checks for files under `src/GarageStack.Data/Migrations/`). Say it in your issue comment too. GarageStack has no built-in backups, so the tester makes their own.
- If the fix is dropped, a migration left in a tester's database stays there. Make the eventual real migration tolerate that (for example `IF NOT EXISTS` in raw SQL), or ask the tester to restore their backup.

---

## How it works

| Piece | What it does |
| ----- | ------------ |
| [`preview-build.yml`](../.github/workflows/preview-build.yml) | Runs on `labeled`, `synchronize` and `reopened` for pull requests to `main`. When the pull request has the `test-build` label and comes from this repository, it calls `docker-publish.yml` with the pull request number, then posts or updates the preview comment (found by the hidden marker `<!-- garagestack-preview-build -->`). |
| [`docker-publish.yml`](../.github/workflows/docker-publish.yml) | The normal image pipeline, callable through `workflow_call` with `pr_number`. For a preview it pushes `pr-<number>` and `pr-<number>-<commit>`, and stamps the version with `.pr.<number>`. It skips the SBOM, provenance and Cosign signature, so each image is a single registry version that the cleanup can find by tag. |
| [`preview-cleanup.yml`](../.github/workflows/preview-cleanup.yml) | Runs when a labelled pull request closes or loses the label, or by hand (*Run workflow* with a pull request number). Deletes every version of the four packages tagged `pr-<number>` or `pr-<number>-<commit>`, and rewrites the preview comment to say so. |
| [`docker-compose.preview.yml`](../docker-compose.preview.yml) | Compose override for testers who run from a clone: drops the `build:` of `api`, `worker` and `frontend` and pulls `ghcr.io/joszz/garagestack-*:${PREVIEW_TAG}` instead. |

Details worth knowing:

- **Which commit is built.** Like CI, the build uses GitHub's merge of the pull request into the current `main`, so the tester also gets everything merged since the branch was made. The `pr-<number>-<commit>` tag names the pull request's own commit.
- **Images per build.** All four: the all-in-one image and the three Compose services. A new push replaces `pr-<number>`; the older build keeps its `pr-<number>-<commit>` tag until the cleanup removes it. Only rebuilding the very same commit (label off and on) leaves the previous build untagged, which costs a little registry space and nothing else.
- **Concurrency.** The build and the cleanup share the group `preview-<number>`: closing a pull request stops a build that is still pushing before the images are deleted. A newer push cancels the build of the previous one.
- **Who can trigger it.** Only people who can label pull requests (triage access and up), and only for branches in this repository. A fork's pull request never gets the packages permission.
- **Update check.** A preview's version is a pre-release of the next patch, so the footer does not offer the current release as an update.
- **Trivy and the security workflow** do not scan previews; they scan `latest` after merge as before.

---

## Troubleshooting

**The label does nothing.** Check that the pull request targets `main`, comes from a branch in this repository (not a fork), and that the label is spelled `test-build`. If the label does not exist yet:

```sh
gh label create test-build --color 5319E7 --description "Publish pr-<number> preview images for testers"
```

**The cleanup fails with 403 or "not found".** The workflow token may delete a package version only when the repository has the *Admin* role on that package. For each of `garagestack`, `garagestack-frontend`, `garagestack-api` and `garagestack-worker`: open the package on GitHub, *Package settings*, *Manage Actions access*, and set this repository to *Admin*. Then run *Preview cleanup* by hand with the pull request number.

**Images are left behind** (for example after a failed cleanup). Run *Preview cleanup* from the Actions tab with the pull request number. To see what is there:

```sh
gh api --paginate "users/joszz/packages/container/garagestack/versions" \
  --jq '.[] | select(any(.metadata.container.tags[]; startswith("pr-"))) | {id, tags: .metadata.container.tags}'
```

**The tester's update fails after the pull request closed.** The images are gone by design. They switch back to `latest`.

**A tester runs the all-in-one image on ARM.** Previews are built for the same platforms as releases (amd64), so the same limits apply.

---

## Before a build: ask for data

A preview build answers "does the fix work on your car". Often the first question is "what does your car actually send", and that needs no build at all:

- Ask the reporter for the relevant columns of `vehicles/{vin}/telemetry.csv` from **Settings > Your data > Download everything**, for one affected day, **without the VIN and the latitude/longitude columns**.
- Turn those rows into unit test fixtures and fix against the real numbers.
- Then publish a preview so the reporter can confirm it on the car.
