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
