# Phone GPS with Dawarich

GarageStack can fill gaps in a car's recorded trips using a phone track already held by
your own [Dawarich](https://dawarich.app/) instance. It only pulls data from Dawarich.
There is no public phone ingest endpoint and no new trip is created from phone data.

## Setup

1. Keep Dawarich running wherever it already runs. It does not need to be inside GarageStack's
   container.
2. Configure a phone app supported by Dawarich, such as OwnTracks, Overland, GPSLogger or the
   Dawarich app, to send locations to Dawarich. Follow Dawarich's instructions for that app.
   Confirm that positions appear on Dawarich's map.
3. In your Dawarich account settings, find or create the API key. Keep it private.
4. Configure GarageStack:
   - **Docker Compose:** put `DAWARICH_URL` and `DAWARICH_API_KEY` in your local `.env`, then
     recreate both API and Worker containers with `docker compose up -d`.
   - **Unraid/all-in-one:** edit the container's Dawarich URL and API key fields (or add those
     environment variables), then apply/recreate the container.
   - The URL is the base address, for example `https://dawarich.example.com`, without `/api/v1`.
     Use the address reachable from the GarageStack containers. With separate containers,
     `localhost` refers to GarageStack itself, not Dawarich. Use a shared Docker network name or
     the host's LAN address and Dawarich's published port.
   - Prefer HTTPS outside a trusted private network. Redirects are refused; supply the final URL.
5. Open GarageStack's **Settings > Phone GPS** tab for the selected vehicle (**GPS** in Dutch and
   Spanish; on a small screen, swipe the tab row sideways to reach it). The tab only appears once
   both environment variables are set. If the connection check fails, it stays so you can retry.
6. Check the connection status and identify your phone in the tracker list. GarageStack shows
   each tracker's ID and last-seen time. The ID is Dawarich's `tracker_id`, falling back to
   `topic` when no tracker ID is supplied; compare it with the device or tracker identity
   configured in your phone app. To distinguish two entries, send a fresh location from the
   phone, wait up to five minutes for the discovery cache to expire, then refresh and check
   which last-seen time advanced. Available trackers come from the last 30 days.
7. Select and save the tracker or trackers for phones you actually use in that car. You can
   select both if both phones travel in it; GarageStack evaluates them separately and uses
   only the best-matching tracker for each trip. Do not select an unrelated household phone.
   If neither `tracker_id` nor `topic` is available, configure a distinct device/tracker
   identity in the app before linking.

The integration uses Bearer authentication. Older Dawarich versions that only accept API keys
in a query string must be upgraded; GarageStack does not put the key into URLs.
The key belongs to one Dawarich user. Multiple trackers from that account can be linked.

## How a trip is filled

The Worker checks every three minutes, at most ten trips per pass. It waits 15 minutes after
a trip ends for the phone's upload. Set `DAWARICH_FUSION_DELAY_MINUTES` to change this.

A tracker must match at least two car fixes within 60 seconds and 300 metres (or twice the
reported accuracy), with at least 70% of paired car fixes agreeing. Fixes with accuracy worse
than 50 metres are discarded. When several linked trackers qualify, only the best is used.
The car wins within 20 seconds of each car fix.

A late start can be recovered only from a known parked spot, with the first phone fix near it.
It never crosses the previous trip's end. Existing anchored points and the end stay in place.
Without an anchor, recovery uses the same car-derived departure estimate as start anchoring,
not the entire time the car was parked. This deliberately excludes unrelated phone movements
earlier in a long stop, even if they started near the car.
Phone stretches appear as purple dots on the maps, with a legend, and the trip detail shows
a phone GPS badge. The route editor can remove phone points. Reset restores the fused route
as recorded, not the sparse route from before fusion.

## Older trips and retries

Linking a tracker only enables trips starting after the link. To fill older trips, select a UTC
**Backfill from** date and choose **Queue backfill**. The Worker processes that range up to the
time of the request, in bounded batches. Edited, hidden and already fused trips are skipped.
Save changed tracker links before requesting a backfill.

An unreachable server, HTTP 429 or server error backs off for at least five minutes. A rejected
key pauses attempts for 30 minutes and logs one warning until authentication recovers. Invalid
responses or excessive data mark that trip failed; request backfill after correcting the cause.
A trip with no matching phone data is not retried automatically: backfill it after late uploads.
Reload the trip list/map after the Worker has processed the trips.

Responses are capped at 4 MiB per page and 100 pages of 1,000 points. A window that exceeds
those limits is rejected, not silently truncated. Discovery errors leave existing links
editable. The client expects Unix timestamps in seconds, UTC query bounds and pagination
headers. These contracts are covered by synthetic tests; verify compatibility with your own
Dawarich version before requesting a large historical backfill.

Clear all linked trackers to stop future fusion and cancel queued backfill. Already stored
phone points remain part of the trips; remove them in the route editor if wanted.

## Privacy and export

Only phone positions accepted into a car trip are persisted. Other fetched fixes exist in
memory only and are discarded. There is no separate phone position table or retention job.
Logs contain counts, outcomes and safe error messages, not coordinates or API keys.
The key stays in deployment configuration, never the database or browser.

The full data export includes linked trackers and saved routes, including phone points and
the original route when manually edited. No Dawarich key is exported.
Existing map services still apply: fused geometry can be sent to your configured map matcher,
and endpoints to your geocoder. Disable `MAPMATCHING__ENABLED` and `GEOCODING__ENABLED`, or
use your own services, if you do not want that geometry leaving your network.

## Battery

Continuous high-accuracy tracking costs battery. Enable it only in the car where practical:
Android can use a Home Assistant or Tasker automation triggered by the car's Bluetooth
connection; iOS can use a Shortcuts automation on CarPlay or Bluetooth connection where the
chosen tracker supports it. Configure a matching stop action when leaving the car.
