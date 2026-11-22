# Phone GPS

GarageStack can fill gaps in a car's recorded trips with a phone's GPS, from either of two
sources:

- **The GarageStack app itself**, installed on the phone and open on screen during the drive.
  Nothing to set up beyond a switch. See [From the GarageStack app](#from-the-garagestack-app).
- **Your own [Dawarich](https://dawarich.app/) instance**, which a tracking app on the phone
  feeds in the background. See [Setup with Dawarich](#setup-with-dawarich).

Both can be used at once. No new trip is created from phone data: phone positions only fill
trips the car recorded, and only once they are shown to have travelled with the car
([How a trip is filled](#how-a-trip-is-filled)).

## From the GarageStack app

A web app can only follow the phone's GPS while it is on screen, so this suits a phone in a
holder with GarageStack open during the drive. It stops while another app, such as a
navigation app, is in front, and starts again when GarageStack is back.

1. Install GarageStack on the phone (the browser's **Install app** or **Add to Home Screen**)
   and open it from the home screen. The settings below only appear in the installed app, and
   only where the browser supports keeping the screen on and location.
2. Open **Settings > General > This device**. Switch on **Keep the screen on**, then
   **Record GPS during drives**, and allow location access when the phone asks.
3. Select the car the phone travels in. The app records for the selected car.

The app sends a fix every five seconds or so while the phone moves (one a minute while it stands
still), leaves out fixes less accurate than 50 metres, and sends them in batches about twice a
minute. Without signal it keeps them on the phone, up to about eight hours of driving, and sends
them once it is back. Both switches belong to that phone only; they do not follow the account to
other devices. If the phone refuses location access, the setting says so: allow it in the
phone's settings, then switch **Record GPS during drives** off and on again.

Behind a reverse proxy that sets its own `Permissions-Policy`, allow `geolocation=(self)`, or
the browser never asks for the location. GarageStack's own nginx sends that.

## Setup with Dawarich

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
5. Open GarageStack's **Settings > Phone GPS** tab for the selected vehicle (**GPS** in Dutch,
   Spanish and Portuguese; on a small screen, swipe the tab row sideways to reach it). The tab only appears once
   both environment variables are set. If the connection check fails, it stays so you can retry.
6. Check the connection status and identify your phone in the tracker list. GarageStack shows
   each tracker's ID and last-seen time. The ID is Dawarich's `tracker_id`, falling back to
   `topic` when no tracker ID is supplied; compare it with the device or tracker identity
   configured in your phone app. To distinguish two entries, send a fresh location from the
   phone, wait up to five minutes for the discovery cache to expire, then refresh and check
   which last-seen time advanced. The list comes from the newest 5,000 points of the last 30
   days, so a phone that has been quiet a while may be missing: type its ID and press Enter.
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
a trip ends for the phone's upload. Set `DAWARICH_FUSION_DELAY_MINUTES` to change this; it
applies to the app's positions as well.

A Dawarich tracker must be linked to the car (above). The app's positions need no link: they
were recorded for the car selected in the app, and the matching below still decides whether
the phone was in it.

A tracker must match at least two car fixes within 300 metres (or twice the reported
accuracy), with at least 70% of paired car fixes agreeing. Where the phone has fixes on both
sides of a car fix, at most two minutes apart, its position at the car's moment is taken on the
straight line between them, so a fast car is not failed only because the phone sampled at other
moments. Otherwise the nearest phone fix within 60 seconds counts. Fixes with accuracy worse
than 50 metres are discarded. When several linked trackers qualify, only the best is used.
The car wins within 20 seconds of each car fix. Dawarich keeps speeds in metres per second;
they are stored in km/h like the car's own.

A late start can be recovered only from a known parked spot, with the first phone fix near it.
It never crosses the previous trip's end. Existing anchored points and the end stay in place.
Without an anchor, recovery uses the same car-derived departure estimate as start anchoring,
not the entire time the car was parked. This deliberately excludes unrelated phone movements
earlier in a long stop, even if they started near the car.
Phone stretches appear as purple dots on the maps, with a legend, and the trip detail shows
a phone GPS badge. On the map page, the **Phone GPS** switch under **Layers** (offered once a
phone has added points to a trip) leaves them out, so the map shows only what the car recorded
itself; the trips themselves keep them. The route editor can remove phone points. Reset
restores the fused route as recorded, not the sparse route from before fusion.

## Older trips and retries

Linking a tracker only enables trips starting after the link. To fill older trips, select a
**Backfill from** date (from midnight in your browser's time zone) and choose **Queue backfill**. The Worker processes that range up to the
time of the request, in bounded batches. Edited, hidden and already fused trips are skipped.
Save changed tracker links before requesting a backfill.

An unreachable server, HTTP 429 or server error backs off for at least five minutes, unless
Dawarich still answers its health check: then the trouble lies in that trip's window, the Worker
moves on to the next trip and retries this one on later passes, marking it failed after five.
A rejected key pauses attempts for 30 minutes and logs one warning until authentication
recovers. Invalid responses or excessive data mark that trip failed at once, without holding up
the next one; request backfill after correcting the cause.
A trip with no matching Dawarich data is not retried automatically: backfill it after late
uploads. Positions the app sends late, after a stretch without signal, put a trip that found no
phone back in the queue by themselves.
Reload the trip list/map after the Worker has processed the trips.

Responses are capped at 4 MiB per page and 100 pages of 1,000 points. A trip window that
exceeds those limits is rejected, not silently truncated. When discovery fails, existing links
stay and a tracker ID can still be typed in. The client expects Unix timestamps in seconds, UTC query bounds and pagination
headers. These contracts are covered by synthetic tests; verify compatibility with your own
Dawarich version before requesting a large historical backfill.

Clear all linked trackers to stop future fusion and cancel queued backfill. Already stored
phone points remain part of the trips; remove them in the route editor if wanted.

## Privacy and export

From Dawarich, only phone positions accepted into a car trip are persisted. Other fetched fixes
exist in memory only and are discarded.

The app's positions are stored as sent, in the `PhonePositions` table, under a random id the
app made for that phone. They are kept 30 days, so a trip can still be filled after a late
upload, and then removed by the Worker's housekeeping. Those accepted into a trip stay part of
it. Sending them needs a signed-in session: a read-only API token cannot. Signing out forgets
the positions still waiting on the phone.
Logs contain counts, outcomes and safe error messages, not coordinates or API keys.
The key stays in deployment configuration, never the database or browser.

The full data export includes linked trackers, the app's stored positions of the last 30 days,
and saved routes, including phone points and the original route when manually edited. No Dawarich key is exported.
Existing map services still apply: fused geometry can be sent to your configured map matcher,
and endpoints to your geocoder. Disable `MAPMATCHING__ENABLED` and `GEOCODING__ENABLED`, or
use your own services, if you do not want that geometry leaving your network.

## Battery

Continuous high-accuracy tracking costs battery. With the app, a screen kept on costs more:
keep the phone on a charger in the car.

For a Dawarich tracker: Enable it only in the car where practical:
Android can use a Home Assistant or Tasker automation triggered by the car's Bluetooth
connection; iOS can use a Shortcuts automation on CarPlay or Bluetooth connection where the
chosen tracker supports it. Configure a matching stop action when leaving the car.
