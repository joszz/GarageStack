# GarageStack

GarageStack is a free, open-source vehicle monitoring dashboard for MG / SAIC cars. It connects to the SAIC iSmart API (the same backend as the official MG iSmart app) and presents your car's live telemetry in a clean, self-hosted web app. The project is designed to work across HEV, PHEV, and BEV variants of the MG lineup — cards that are not relevant to your vehicle type are automatically hidden or adapted.

Features include a live dashboard, trip history with map and heatmap visualisation, energy statistics, and remote commands (climate, lock/unlock, find-my-car). The app is a PWA, so it can be installed on your phone or desktop and receives push notifications.

Security defaults:

- API routes require login.
- Login reuses the configured MG account credentials (`SAIC_USER`/`SAIC_PASSWORD`) and issues short-lived JWT tokens.
- MQTT now requires credentials and ACLs, and broker exposure defaults to localhost-only in Docker Compose.

---

## Recommended IDE Setup

[VS Code](https://code.visualstudio.com/) + [Vue (Official)](https://marketplace.visualstudio.com/items?itemName=Vue.volar) (and disable Vetur).

## Recommended Browser Setup

- Chromium-based browsers (Chrome, Edge, Brave, etc.):
  - [Vue.js devtools](https://chromewebstore.google.com/detail/vuejs-devtools/nhdogjmejiglipccpnnnanhbledajbpd)
  - [Turn on Custom Object Formatter in Chrome DevTools](http://bit.ly/object-formatters)
- Firefox:
  - [Vue.js devtools](https://addons.mozilla.org/en-US/firefox/addon/vue-js-devtools/)
  - [Turn on Custom Object Formatter in Firefox DevTools](https://fxdx.dev/firefox-devtools-custom-object-formatters/)

## Type Support for `.vue` Imports in TS

TypeScript cannot handle type information for `.vue` imports by default, so we replace the `tsc` CLI with `vue-tsc` for type checking. In editors, we need [Volar](https://marketplace.visualstudio.com/items?itemName=Vue.volar) to make the TypeScript language service aware of `.vue` types.

## Customize configuration

See [Vite Configuration Reference](https://vite.dev/config/).

## Project Setup

```sh
pnpm install
```

### Compile and Hot-Reload for Development

```sh
pnpm run dev
```

### Type-Check, Compile and Minify for Production

```sh
pnpm run build
```

### Run Unit Tests with [Vitest](https://vitest.dev/)

```sh
pnpm run test:unit
```

### Lint with [ESLint](https://eslint.org/)

```sh
pnpm run lint
```
