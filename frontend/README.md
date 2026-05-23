# GarageStack Frontend

GarageStack is a free, open-source vehicle monitoring dashboard for MG / SAIC cars. It connects to the SAIC iSmart API (the same backend as the official MG iSmart app) and presents your car's live telemetry in a clean, self-hosted web app. The project is designed to work across HEV, PHEV, and BEV variants of the MG lineup — cards that are not relevant to your vehicle type are automatically hidden or adapted.

Features include a live dashboard, trip history with map and heatmap visualisation, energy statistics, and remote commands (climate, lock/unlock, find-my-car). The app is a PWA, so it can be installed on your phone or desktop and receives push notifications.

---

## Dashboard cards explained

### EV Battery vs HV Battery

These two cards both relate to the high-voltage battery in the drivetrain, but at different levels of detail and from different API fields:

| Card | Label | What it shows | Relevant for |
|------|-------|---------------|--------------|
| **EV Battery** | `evSocPercent` | State of charge as a simple percentage | PHEV, BEV |
| **HV Battery** | `hvSocKwh` + detail | kWh remaining, total capacity, voltage, current, power flow | HEV, PHEV, BEV |

**On a PHEV or BEV** the EV Battery percentage is the most immediately useful number -- it tells you how much electric range you have left, much like a fuel gauge. The HV Battery card drills into the same pack with engineering detail (exact kWh, live charging current, etc.) and adds the charge-current-limit control for PHEV/BEV owners.

**On an HEV (like the MG HS Hybrid)** there is no plug, so the EV Battery percentage is less meaningful day-to-day: the hybrid battery is small (typically 1-2 kWh) and the car manages its charge level automatically through regenerative braking and the engine. The HV Battery card is more useful here because it shows the actual kWh value and live power flow, letting you see regeneration and motor-assist happening in real time. The charge-current controls and the "Since Charge" efficiency card are hidden automatically for HEV.

### Energy today

**Label:** `vehicle.efficiency.todayEnergy` -- shown in Wh (watt-hours)

This is the total electrical energy that has passed through the high-voltage battery system since midnight. On a BEV it maps directly to how much battery charge you have consumed. On a PHEV it covers both grid charge consumed and regeneration. On an HEV it reflects the combined regeneration and motor-assist energy that cycled through the small hybrid battery -- useful as a relative indicator of how aggressively the hybrid system was working, but not a measure of plug energy since there is none.

### Efficiency

**Label:** `vehicle.efficiency.efficiency` -- shown in Wh/km

This is Energy today divided by Distance today: a measure of how much electrical energy the drivetrain consumed per kilometre. Lower is better. On a BEV or PHEV in EV mode this translates directly to electricity cost per km. On an HEV it gives a sense of how efficiently the hybrid system ran (motorway driving at constant speed will show a much lower Wh/km than stop-start city driving, where regeneration cycles are frequent but efficiency losses add up).

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
