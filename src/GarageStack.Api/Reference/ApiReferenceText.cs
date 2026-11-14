namespace GarageStack.Api.Reference;

/// <summary>
/// The words the API reference shows beside the endpoints: its introduction, and what each group
/// of endpoints (an OpenAPI tag) is for. Markdown, rendered by the reference page.
/// </summary>
internal static class ApiReferenceText
{
    // The same text whatever line endings the source was checked out with, so the committed
    // document (documentation/openapi.json) matches on every platform.
    public static readonly string Introduction = """
        GarageStack's own API: everything the web app shows and does, for scripts, Home Assistant and
        anything else you connect.

        ## Signing in

        - **On this page**, *Test Request* sends the request as you, with the session of the browser
          you signed in with. It is the real thing: a command goes to the car, a delete deletes.
        - **From a script**, make a read-only token under *Settings, API tokens* and send it as
          `Authorization: Bearer gs_...`. A token only reads (GET), and never the export, the logs
          or the tokens themselves.
        - **The Homepage widget** sends the `X-Widget-Key` header (WIDGET_API_KEY) instead.

        ## Answers

        - Times are UTC, in ISO 8601. Distances are km, speeds km/h, temperatures °C, tyre pressures
          bar, energy kWh and power kW, whatever units the web app is set to show.
        - A refusal is a problem details body (RFC 9457) with a `code`, such as `trip.changed`, that
          stays the same between releases; `detail` says it in English.
        - Each address may make 120 requests a minute (RATE_LIMIT_GLOBAL_PER_MINUTE), and some
          endpoints allow fewer. Past that the answer is 429.
        - A command's outcome arrives later, over the SignalR hub at `/hubs/telemetry`, as the car
          answers it. The command endpoint only says it was sent.
        - `GET /api/health` reports the database, the MQTT broker and the Worker, without signing in.
        """.ReplaceLineEndings("\n");

    /// <summary>Every tag an endpoint uses, with what its endpoints are for. A test holds the two to each other.</summary>
    public static readonly IReadOnlyDictionary<string, string> Tags = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Authentication"] = "Signing in and out, and which sign-in methods this install offers.",
        ["Vehicles"] = "The cars: their latest telemetry, statistics, charge sessions and tyre pressures, and the commands sent to them.",
        ["Trips"] = "Trips as recorded, and the trip log: purposes, notes, hiding, joining and editing a route.",
        ["Maintenance"] = "Service items with their intervals and when they are due, and each item's service history.",
        ["Climate schedules"] = "Climate runs the Worker starts at set times on set days.",
        ["Commute places"] = "Home and work places. Trips between them are labelled as commutes, unless someone chose a purpose.",
        ["RDW"] = "The Dutch registration: APK, insurance and recalls. Answers 404 `rdw.disabled` unless RDW_ENABLED is set.",
        ["Dawarich"] = "Phone GPS from Dawarich, merged into trips. Answers 404 `dawarich.disabled` unless Dawarich is configured.",
        ["Map"] = "Charging stations (Open Charge Map), points of interest, addresses and map matching (OpenStreetMap services), all cached.",
        ["Notifications"] = "The alerts the Worker raised, as the bell lists them.",
        ["Push Notifications"] = "Browser push subscriptions (Web Push).",
        ["Settings"] = "The signed-in account's settings, kept per section so they follow it to every device.",
        ["API tokens"] = "Read-only tokens for scripts. Made and revoked from a signed-in session only.",
        ["Export"] = "Everything GarageStack keeps, as one zip.",
        ["Logs"] = "The API's and the Worker's log entries, as the log viewer pages through them.",
        ["Updates"] = "Whether a newer GarageStack release is out.",
        ["Diagnostics"] = "Errors the web app ran into in the browser, for the API's log.",
        ["Widget"] = "The car's status in one call, for a Homepage dashboard widget.",
        ["Demo"] = "Demo mode only: change the demo car's state, as the Demo Controls panel does.",
    };
}
