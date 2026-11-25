# Dev container and Codespaces

GarageStack comes with a ready-made development environment: a container with .NET 11, Node 26, the pinned pnpm and a PostgreSQL, set up the way CI runs. You can run it in the cloud with GitHub Codespaces, with nothing on your own machine but a browser, or locally with Docker and VS Code. Either way you get the same tools, the same versions and the same VS Code tasks.

## Contents

- [What it is](#what-it-is)
- [What is in it](#what-is-in-it)
- [GitHub Codespaces](#github-codespaces)
- [On your own machine](#on-your-own-machine)
- [Working in it](#working-in-it)
- [What it cannot do](#what-it-cannot-do)
- [Changing the container](#changing-the-container)
- [Troubleshooting](#troubleshooting)

## What it is

A **dev container** is a Docker container that holds everything a project needs to be built, run and tested. Its recipe lives in the repository, in `.devcontainer/`, so everyone who opens it gets the same setup. VS Code connects to the container and works inside it: the terminal, the extensions, the debugger and the running app are all in the container, while the editor window looks the same as always.

**GitHub Codespaces** runs that same container on a virtual machine at GitHub. You open it from the repository page and work in VS Code in the browser, or in VS Code on your desktop connected to it. Nothing is installed on your computer, and the codespace keeps your changes until you delete it.

The two are the same container: Codespaces runs it in the cloud, the Dev Containers extension runs it on your own Docker.

## What is in it

The recipe is [`.devcontainer/devcontainer.json`](../.devcontainer/devcontainer.json), with [`.devcontainer/compose.yml`](../.devcontainer/compose.yml) next to it.

| Part | What it does |
|------|--------------|
| `app` service | The container you work in: Ubuntu 24.04 (`mcr.microsoft.com/devcontainers/base`) with the repository at `/workspaces/garagestack` |
| .NET feature | The .NET 11 SDK |
| Node feature | Node 26 and pnpm 12.21.0, the version in `packageManager` in `frontend/package.json` |
| `db` service | PostgreSQL 18, only for the backend tests in `src/GarageStack.Tests/Postgres`. Demo mode needs no database |
| `node-modules` volume | `frontend/node_modules` lives in a volume of its own, so a Windows or macOS checkout keeps the packages it installed on the host |
| `GARAGESTACK_TEST_POSTGRES` | Points the Postgres tests at the `db` service, so they run instead of being skipped |
| `__VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS` | Lets Vite answer on Codespaces' `*.app.github.dev` addresses |
| Ports | 5173 (the frontend, opened in the browser) and 5000 (the API) |
| `postCreateCommand` | Runs once, when the container is first made: `pnpm install`, `dotnet tool restore` and `dotnet restore` |
| Extensions | C# Dev Kit, Vue, ESLint, oxc, Stylelint, Prettier, EditorConfig, Vitest and Playwright |

The compose project is called `garagestack-devcontainer`, so it never mixes with a GarageStack stack (`garagestack`) on the same machine.

## GitHub Codespaces

### Create one

1. On the repository page, pick the branch you want to work on in the branch menu. For a pull request, pick its branch.
2. Click **Code**, open the **Codespaces** tab, and click **+** (**Create a codespace on _branch_**).
3. The first start takes a few minutes: GitHub builds the container, then runs the `postCreateCommand`. A terminal shows its progress.

A contributor without write access does the same on their fork.

The [GitHub CLI](https://cli.github.com/) can do it too:

```bash
gh codespace create -R joszz/GarageStack -b <branch>   # create one
gh codespace code                                     # open it in VS Code on your desktop
gh codespace code --web                               # or in the browser
gh codespace stop                                     # stop it
gh codespace delete                                   # delete it
```

### Check that it works

In the codespace's terminal:

```bash
dotnet --version   # 11.x
node -v            # v26.x
pnpm -v            # 12.21.0
```

Then run the **Demo** task and the **Check** task as described in [Working in it](#working-in-it).

### What it costs

Codespaces is billed to the account that creates the codespace; the dialog says who pays before you create one. A codespace from a fork of this repository is billed to your own account.

A personal account gets a free amount each month: on GitHub Free, 120 hours of compute and 15 GB-month of storage; on GitHub Pro, 180 hours and 20 GB-month. The hours count per processor core, so the default 2-core machine uses up the 120 hours in 60 hours of real time. Your account's billing settings show what is left. See [About billing for GitHub Codespaces](https://docs.github.com/en/billing/managing-billing-for-your-products/managing-billing-for-github-codespaces/about-billing-for-github-codespaces).

### Stop and delete

- A codespace **stops by itself** after 30 minutes without activity. Change that under **Settings > Codespaces > Default idle timeout** (5 minutes to 4 hours). A stopped codespace uses no compute, only storage.
- A stopped codespace is **deleted by itself** after 30 days without use. Under **Settings > Codespaces** you can shorten that to anything down to 0 days.
- To delete one now: on [github.com/codespaces](https://github.com/codespaces), open its **...** menu and choose **Delete**, or run `gh codespace delete`. Anything you did not push is lost with it.

### Ports and addresses

When the frontend starts, Codespaces forwards port 5173 and opens it in a browser tab at an address like `https://<codespace-name>-5173.app.github.dev`. Forwarded ports are **private** by default: only you, signed in to GitHub, can open them. The **Ports** panel (next to the terminal) lists them, and right-clicking one changes its visibility. Leave them private: the demo login is demo / demo.

## On your own machine

You need [Docker](https://www.docker.com/products/docker-desktop/) (Docker Desktop on Windows and macOS) and VS Code with the **Dev Containers** extension (`ms-vscode-remote.remote-containers`, which the repository recommends).

1. Open the repository folder in VS Code.
2. VS Code offers **Reopen in Container**. If it does not, open the Command Palette (`Ctrl+Shift+P`, `Cmd+Shift+P` on macOS) and run **Dev Containers: Reopen in Container**.
3. The first start builds the container and installs the packages, which takes a few minutes. Later starts take seconds.
4. **Dev Containers: Reopen Folder Locally** goes back to working on the host.

Your own checkout is mounted into the container, so edits on either side are the same files. Two things to know:

- `frontend/node_modules` is kept apart in a volume, so the container's Linux packages and your host's packages never overwrite each other.
- The backend's `bin/` and `obj/` folders are shared. After working in the container, the first `dotnet build` on the host restores again. That is expected.

## Working in it

Everything runs from **Terminal > Run Task**:

| Task | What it does |
|------|--------------|
| **Demo** | Starts the API on sample data (the `Demo` launch profile), waits until it listens, then starts the frontend. Open port 5173 and sign in with **demo / demo** |
| **API (demo)** / **Frontend (demo)** | The two halves of **Demo**, on their own |
| **Check** | What CI runs: `pnpm check` in `frontend/`, the backend tests and the backend format check |
| **Check: frontend** / **Check: backend tests** / **Check: backend format** | One part of **Check** |
| **Build API** | `dotnet build` of the API |

To debug the API, choose **API (demo)** under **Run and Debug** and start the **Frontend (demo)** task next to it. [DEMO.md](DEMO.md) says what the demo can do, including the Demo Controls for driving the demo car.

The same tasks work outside the container, on Windows, macOS and Linux, as long as .NET, Node and pnpm are installed.

Git works as usual inside the container. In Codespaces you are already signed in to GitHub, so `git push` works straight away.

## What it cannot do

- **No Docker inside.** The browser tests in `frontend/e2e` run against the demo Docker stack (see [CONTRIBUTING.md](../.github/CONTRIBUTING.md)), so run them on a machine with Docker. CI runs them on every pull request.
- **No car.** There is no MQTT broker or SAIC gateway in the container. Demo mode covers almost everything; for a change that needs a real car, see [PREVIEW_BUILDS.md](PREVIEW_BUILDS.md).

## Changing the container

After a change to anything in `.devcontainer/`, rebuild: **Codespaces: Rebuild Container** in a codespace (or `gh codespace rebuild`), **Dev Containers: Rebuild Container** locally. A rebuild keeps the repository and your changes; it remakes the container and runs `postCreateCommand` again.

When pnpm moves to a new version, change `pnpmVersion` in `devcontainer.json` together with `packageManager` in `frontend/package.json`. They are the only two places it is pinned.

## Troubleshooting

**The container fails to start.** Codespaces then opens in a recovery container and offers to show the creation log; locally, VS Code's error message leads to the log. The usual cause is the `postCreateCommand`, for example a package that failed to download. Fix the cause, or retry, and rebuild.

**The Postgres tests are skipped.** They run only when `GARAGESTACK_TEST_POSTGRES` is set. Check it with `echo $GARAGESTACK_TEST_POSTGRES` in the terminal. If it is empty, the container was made before that setting was added: rebuild it.

**Vite answers "Blocked request. This host is not allowed".** The container sets `__VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS` for that. A codespace made before it was added needs a rebuild.

**The Demo task never starts the frontend.** It waits for the API to print `Now listening on`. Look at the **API (demo)** terminal: a build error stops it before that line.

**The port does not open.** Open the **Ports** panel and click the address for 5173. If 5173 is missing, the frontend has not started yet: check the **Frontend (demo)** terminal.
