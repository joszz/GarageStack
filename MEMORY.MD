# Repository context

- Dawarich is pull-only and opt-in per vehicle. Keep credentials in deployment configuration,
  shared by API and Worker, and never persist or log rejected phone positions.
- Phone fusion is part of a trip as recorded. It uses the route-summary update path without
  setting `EditedAt` or `OriginalPointsJson`; an editor reset restores the fused route.
- Historical phone fusion requires an explicit backfill. Normal processing respects tracker
  link dates; unlinking cancels queued work. Recheck edits and links after the upstream fetch.
- Bound late-start recovery by the car-derived departure window, not a whole parked interval.
  The phone may refine a known drive, but must not create one.

Implementation: [fusion helper](src/GarageStack.Core/Helpers/TripPhoneFusion.cs),
[persistence](src/GarageStack.Data/Repositories/PhoneFusionRepository.cs),
[setup guide](documentation/PHONE_GPS.md).

## Directions to the car

- Keep directions options in the shared dialog, opened by the dashboard map's top-right
  directions icon or the single directions button in the current-car marker popup.
- OpenStreetMap walking directions (`/directions?engine=fossgis_osrm_foot&to=lat,lng`) are the
  primary option, in keeping with the FOSS map stack; Google Maps, Apple Maps and the `geo:` link
  are the alternatives below it.
- Use only the selected vehicle's validated telemetry coordinates, not phone fixes or trip ends.
  The vehicle-state update time is not necessarily the GPS-fix time; merged telemetry can retain
  an old speed after the engine reports off. External maps receive coordinates only on user action.

## Settings tabs

- No Bootstrap or Sass: the tabs are plain CSS in `frontend/src/assets/settings.css`, styled
  off `aria-selected`, and match the earlier Bootstrap nav-tabs look pixel for pixel. Tabs sit
  beside the title at every screen size. Give each tab generous horizontal padding, cap the
  modal at 771px, fit every locale on desktop, and keep the row swipeable on narrower screens.
- Dutch and Spanish use a short GPS tab label with a full accessible name and tooltip. Check
  desktop fit with wider system fonts such as Linux DejaVu Sans as well as mobile browsers.
- Keep tab panels mounted while switching so unsaved inputs and newly issued API tokens survive.
  Hide inactive panels with `hidden`, not inline styles, and exclude them from dialog focus loops.
- Phone GPS reports its visibility from the same state that renders its content. Disabled
  deployments hide the tab; errors and in-flight retries remain accessible. Reset on vehicle changes.
- Block service workers in API-mocked browser tests, since worker-owned requests can bypass
  Playwright's route mocks.
