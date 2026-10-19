# Screenshots

The main pages of GarageStack, on a desktop and on a phone. They were taken in [demo mode](../DEMO.md), so the car, its trips and the addresses are sample data.

## Desktop

### Dashboard

![The dashboard: the car on the road with its speed, tyre pressures and charge, a map of where it is, and cards for the battery, range, doors, windows, climate and today's driving](desktop-dashboard.webp)

### Map

![The map: a month of trips listed by where they went, drawn along the roads between Amsterdam, Haarlem, Almere, Utrecht and Amersfoort over a heatmap of the most driven roads](desktop-map.webp)

### A single trip

![One trip from Amsterdam to Utrecht, snapped to the roads and coloured green where it kept to the speed limit and red where it went over, with a legend summing up how much of it was over](desktop-map-trip.webp)

### Statistics

![The statistics: insight cards for the month's distance, average trip length, peak drive time, 12V trend, parking spots and average speed, above charts of the EV battery and the tyre pressures](desktop-statistics.webp)

### Trip log

![The trip log: totals per purpose for the month, and each trip with its addresses, odometer readings, a business, commute or private choice, a notes field, and buttons to hide it or join it with the next trip](desktop-trip-log.webp)

### Maintenance

![The maintenance list: service items marked overdue, due soon, OK or not tracked yet](desktop-maintenance.webp)

### Climate schedules

![The climate schedules: a weekday schedule at 07:30 with seat heating, and a one-off front defrost at 08:15, each with when it runs next](desktop-climate-schedules.webp)

### Light theme

![The dashboard in the light theme](desktop-dashboard-light.webp)

## Phone

<!-- markdownlint-disable MD033 -->

| Dashboard | Map | A single trip | Light theme |
| --------- | --- | ------------- | ----------- |
| <img src="mobile-dashboard.webp" alt="The dashboard on a phone" width="180"> | <img src="mobile-map.webp" alt="The map on a phone" width="180"> | <img src="mobile-map-trip.webp" alt="One trip against the speed limits on a phone" width="180"> | <img src="mobile-dashboard-light.webp" alt="The dashboard on a phone in the light theme" width="180"> |
| **Statistics** | **Trip log** | **Maintenance** | **Climate schedules** |
| <img src="mobile-statistics.webp" alt="The statistics on a phone" width="180"> | <img src="mobile-trip-log.webp" alt="The trip log on a phone" width="180"> | <img src="mobile-maintenance.webp" alt="The maintenance list on a phone" width="180"> | <img src="mobile-climate-schedules.webp" alt="The climate schedules on a phone" width="180"> |

<!-- markdownlint-enable MD033 -->

## Taking new ones

`pnpm screenshots` in `frontend/` takes all of them again from a running demo stack, and copies the dashboard, map and statistics ones to the install screenshots in the app's manifest (`frontend/public/screenshot-*.webp`):

```bash
cp .env.demo.example .env.demo
docker compose --env-file .env.demo -f docker-compose.demo.yml up -d --build   # repository root
cd frontend && pnpm screenshots                                                 # or: pnpm screenshots http://localhost:5173
```

It signs in as `demo`, and takes the desktop screenshots at 1280 x 800 and the phone screenshots at 390 x 844 at twice the pixel density, in the dark theme with the date filter on 30 days and the region set to the Netherlands, plus the dashboard in the light theme. The maps need the internet for their tiles; behind a proxy, `HTTPS_PROXY` is used for them. `CHROMIUM_PATH` points it at a Chromium other than Playwright's own. Keep the manifest's `sizes` in `frontend/vite.config.ts` in step if the sizes change.
