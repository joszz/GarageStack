# Screenshots

The main pages of GarageStack, on a desktop and on a phone. They were taken in [demo mode](../DEMO.md), so the car, its trips and the addresses are sample data.

## Desktop

### Signing in

![The sign-in page: a card with the GarageStack logo, a username and a password field, a Remember me switch to stay logged in for 30 days, and the Log in button](desktop-login.webp)

### Dashboard

![The dashboard: the car on the road with its speed, tyre pressures and charge, a map of where it is, and cards for the battery, range, doors, windows, climate and today's driving](desktop-dashboard.webp)

### The map behind the dashboard

![The dashboard with the car's map across the whole page: the overview and the cards float over it, and the car is drawn where it is parked, in the spot the location card would take](desktop-dashboard-map-backdrop.webp)

### Changing the layout

![The dashboard in edit mode: every card has a handle to drag it and a button to hide it, and Reset sits beside Done in the page header](desktop-dashboard-edit.webp)

### Without a connection

![The dashboard with no connection to GarageStack: a notice above the cards says the page shows what this device last loaded, and the Refresh button is gone](desktop-dashboard-offline.webp)

### High-voltage battery

![The HV Battery dialog of the electric car: the energy in the pack, its capacity, voltage, current and power, the energy used since the last charge, the charger state, the charge current limit buttons and the charge target slider](desktop-hv-battery.webp)

### Map

![The map: a month of trips listed by where they went, each leaving from where the one before it ended, drawn along the roads from Amsterdam to Haarlem, Almere, Utrecht, Amersfoort, The Hague and Delft over a heatmap of the most driven roads](desktop-map.webp)

### A single trip

![One trip from Amsterdam to The Hague, snapped to the roads and coloured green where it kept to the speed limit, red where it went over and grey where OpenStreetMap knows no limit, with a legend summing up how much of it was over](desktop-map-trip.webp)

### Statistics

![The statistics: insight cards for the month's distance, average trip length, energy used, efficiency, remote preconditioning, peak drive time, 12V trend, parking spots and average speed, above charts of the EV battery and the tyre pressures](desktop-statistics.webp)

### Energy and efficiency

![Further down the statistics: the energy used per day in kWh as bars, with the efficiency in kWh/100 km as a line over them](desktop-statistics-energy.webp)

### Trip log

![The trip log: the month under the title with a filters button beside it, totals per purpose, a compact list of trips with their purpose, times, addresses and distance, and beside it the selected trip's details with its route on a map](desktop-trip-log.webp)

### Editing a trip's route

![The route editor: a trip from Amsterdam to Schiphol on a map with green start and red end markers, and beside it the distance, the addresses, the choice of moving the start, the end or points in between, and Undo, Reset to recorded, Cancel and Save](desktop-trip-edit.webp)

### Charge history

![The charge history: totals for the sessions, the battery energy gained and the time spent charging over the last 90 days, above a compact list of sessions with their times, duration, charge levels, peak power and energy, and beside it the selected session's details with where it charged on a map and its figures](desktop-charge-history.webp)

### Maintenance

![The maintenance list: service items marked overdue, due soon, OK or not tracked yet](desktop-maintenance.webp)

### Climate schedules

![The climate schedules: a weekday schedule at 07:30 with seat heating, and a one-off front defrost at 08:15, each with when it runs next](desktop-climate-schedules.webp)

### Settings

![The settings dialog on its General tab, with tabs for Vehicle, Map, Phone GPS, Notifications and Data & API, and the appearance (Light, Dark or System), dashboard, language and region settings](desktop-settings.webp)

### Settings for each car

![The Vehicle tab of the settings: the car's colour, its type as the gateway reports it, the power tailgate switch, and the car's own battery capacity, tyre pressure bands and warning levels, each empty field showing the server's default](desktop-settings-vehicle.webp)

### Logs

![The log viewer showing the Worker's entries: trips saved, phone GPS added to a trip, a commute arrival, an RDW notification, and a climate command the car refused while busy, then accepted](desktop-logs.webp)

### Light theme

![The dashboard in the light theme](desktop-dashboard-light.webp)

## Phone

<!-- markdownlint-disable MD033 -->

| Dashboard | Map | A single trip | Light theme |
| --------- | --- | ------------- | ----------- |
| <img src="mobile-dashboard.webp" alt="The dashboard on a phone" width="180"> | <img src="mobile-map.webp" alt="The map on a phone" width="180"> | <img src="mobile-map-trip.webp" alt="One trip against the speed limits on a phone" width="180"> | <img src="mobile-dashboard-light.webp" alt="The dashboard on a phone in the light theme" width="180"> |
| **Statistics** | **Trip log** | **Editing a route** | **Charge history** |
| <img src="mobile-statistics.webp" alt="The statistics on a phone" width="180"> | <img src="mobile-trip-log.webp" alt="The trip log on a phone" width="180"> | <img src="mobile-trip-edit.webp" alt="The route editor on a phone, the map above the controls" width="180"> | <img src="mobile-charge-history.webp" alt="The charge history on a phone: the period's totals above the list of charge sessions" width="180"> |
| **Maintenance** | **Climate schedules** | **Settings** | **Logs** |
| <img src="mobile-maintenance.webp" alt="The maintenance list on a phone" width="180"> | <img src="mobile-climate-schedules.webp" alt="The climate schedules on a phone" width="180"> | <img src="mobile-settings.webp" alt="The settings on a phone, the tabs as icons under the title" width="180"> | <img src="mobile-logs.webp" alt="The Worker's log entries on a phone" width="180"> |
| **Changing the layout** | **Without a connection** | **High-voltage battery** | **Energy and efficiency** |
| <img src="mobile-dashboard-edit.webp" alt="The dashboard in edit mode on a phone, opened with a long press on a card, with Reset and Done in a bar floating at the bottom" width="180"> | <img src="mobile-dashboard-offline.webp" alt="The dashboard on a phone with no connection, showing the car as this device last loaded it" width="180"> | <img src="mobile-hv-battery.webp" alt="The HV Battery dialog on a phone, with the charge current limit and the charge target" width="180"> | <img src="mobile-statistics-energy.webp" alt="The energy used per day and the efficiency on a phone" width="180"> |
| **Settings for each car** | **Signing in** | | |
| <img src="mobile-settings-vehicle.webp" alt="The Vehicle tab of the settings on a phone" width="180"> | <img src="mobile-login.webp" alt="The sign-in page on a phone" width="180"> | | |

<!-- markdownlint-enable MD033 -->

## Taking new ones

`pnpm screenshots` in `frontend/` takes all of them again from a running demo stack (the sign-in page before it signs in, the map behind the dashboard switched on for its shot and off again, the layout editor opened from the header on a desktop and by a long press on a phone, the browser taken offline for the offline shot, the route editor opened from the newest trip, the newest charge session opened on a desktop, the settings on their first tab and on the Vehicle tab, the log viewer on the Worker's entries), and copies the dashboard, map and statistics ones to the install screenshots in the app's manifest (`frontend/public/screenshot-*.webp`):

```bash
cp .env.demo.example .env.demo
docker compose --env-file .env.demo -f docker-compose.demo.yml up -d --build   # repository root
cd frontend && pnpm screenshots                                                 # or: pnpm screenshots http://localhost:5173
pnpm screenshots http://localhost:8080 dashboard-edit settings-vehicle          # only these, on desktop and phone
```

Against the Vite dev server, start it with `VITE_DEMO_MODE=false`, or the demo banner and Demo Controls end up in the screenshots.

It signs in as `demo`, and takes the desktop screenshots at 1280 x 800 and the phone screenshots at 390 x 844 at twice the pixel density, in the dark theme with the date filter on 30 days and the region set to the Netherlands, plus the dashboard in the light theme. The maps need the internet for their tiles; behind a proxy, `HTTPS_PROXY` is used for them. `CHROMIUM_PATH` points it at a Chromium other than Playwright's own. Keep the manifest's `sizes` in `frontend/vite.config.ts` in step if the sizes change.
