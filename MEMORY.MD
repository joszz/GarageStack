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
- Use only the selected vehicle's validated telemetry coordinates, not phone fixes or trip ends.
  The vehicle-state update time is not necessarily the GPS-fix time; merged telemetry can retain
  an old speed after the engine reports off. External maps receive coordinates only on user action.
