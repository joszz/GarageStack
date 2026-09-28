# Screenshots

Every view of GarageStack, on a desktop and on a phone. They were taken in [demo mode](../DEMO.md), so the car, its trips and the addresses are sample data.

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

![The trip log: totals per purpose for the month, and each trip with its addresses, odometer readings, a business, commute or private choice and a notes field](desktop-trip-log.webp)

### Maintenance

![The maintenance list: service items marked overdue, due soon, OK or not tracked yet](desktop-maintenance.webp)

### Light theme

![The dashboard in the light theme](desktop-dashboard-light.webp)

## Phone

<!-- markdownlint-disable MD033 -->

| Dashboard | Map | A single trip | Light theme |
| --------- | --- | ------------- | ----------- |
| <img src="mobile-dashboard.webp" alt="The dashboard on a phone" width="180"> | <img src="mobile-map.webp" alt="The map on a phone" width="180"> | <img src="mobile-map-trip.webp" alt="One trip against the speed limits on a phone" width="180"> | <img src="mobile-dashboard-light.webp" alt="The dashboard on a phone in the light theme" width="180"> |
| **Statistics** | **Trip log** | **Maintenance** | |
| <img src="mobile-statistics.webp" alt="The statistics on a phone" width="180"> | <img src="mobile-trip-log.webp" alt="The trip log on a phone" width="180"> | <img src="mobile-maintenance.webp" alt="The maintenance list on a phone" width="180"> | |

<!-- markdownlint-enable MD033 -->

## Taking new ones

The desktop screenshots are 1280 x 800 and the phone screenshots 390 x 844 at twice the pixel density, in the dark theme with the date filter on 30 days. The dashboard, map and statistics screenshots are also the install screenshots in the app's manifest, as `frontend/public/screenshot-*.webp`: replace those too, and keep their `sizes` in `frontend/vite.config.ts` in step.
