# The API

The web app does everything through GarageStack's own REST API, and anything else can use the same API: a script, Home Assistant, a dashboard. This page covers the reference that describes it, and what a developer keeps up to date when changing it.

## The reference

Open `/api/reference/` on the address you use for GarageStack, or follow **API reference** under **Settings > Data & API > API tokens**. It lists every endpoint with what it takes and what it answers, and asks you to sign in first if you have not.

A deployment serves the page once `API_REFERENCE_ENABLED=true` is set; [demo mode](DEMO.md) and development serve it unless that says `false`. It is off by default because it runs a third-party library, Scalar, on GarageStack's own address with your session, and most installs never need it. Off, `/api/reference/` answers 404 and the settings show no link. The OpenAPI document below is served either way.

*Test Request* on any endpoint sends the request with your own session: nothing to fill in, and it is the real thing. A command goes to the car, a delete deletes. In [demo mode](DEMO.md) it goes to the demo car.

The page is [Scalar](https://scalar.com), served by the API itself. It loads nothing from elsewhere: no fonts or scripts from a CDN, no telemetry, and Scalar's AI and hosted-client features are switched off.

The same description is an OpenAPI 3.1 document:

- `/api/openapi/v1.json` on a running GarageStack, for a signed-in session or an [API token](../README.md#read-only-api-tokens). Code generators and other OpenAPI tools take it as it is.
- [`openapi.json`](openapi.json) in this folder, as of the commit you are looking at. It is the quickest way to learn the API without running anything, for people and coding agents alike.

### Behind your own reverse proxy

The reference page comes with a Content-Security-Policy of its own, built around a new nonce on every request. The nginx in the GarageStack images leaves its own policy off that page for that reason. A proxy in front that adds or replaces a Content-Security-Policy on every response (a Traefik headers middleware, for one) has to leave `/api/reference` alone as well, or the page stays blank.

## Signing in

| Who | How | What it may do |
| --- | --- | --- |
| The browser, and the reference page | The session cookie that signing in sets | Everything |
| A script | `Authorization: Bearer gs_...`, a read-only token from Settings | GET only, and not the export, the logs or the tokens. See [read-only API tokens](../README.md#read-only-api-tokens) |
| The Homepage widget | `X-Widget-Key: <WIDGET_API_KEY>` | `/api/widget` only. See [`WIDGET.md`](WIDGET.md) |

The reference marks each endpoint with the ways in it accepts, so an endpoint a token cannot call says so.

## Answers

- Times are UTC, in ISO 8601. Distances are km, speeds km/h, temperatures °C, tyre pressures bar, energy kWh and power kW, whatever units the app is set to show.
- A refusal is a [problem details](https://www.rfc-editor.org/rfc/rfc9457) body with a `code`, such as `trip.changed` or `token.readOnly`. The code stays the same between releases; `detail` says the same in English. The web app translates the code.
- A request from a page on another address than `CORS_ORIGIN` that would change something is refused with `403` and `csrf.originNotAllowed`. A script sends no `Origin` header and is not affected.
- Each address may make 120 requests a minute (`RATE_LIMIT_GLOBAL_PER_MINUTE`), and some endpoints allow fewer. Past that the answer is `429`.
- A command (`POST /api/vehicles/{vin}/commands/{command}`) answers once it is sent. The car's answer arrives later over the SignalR hub at `/hubs/telemetry`, or shows in the next telemetry.

## Changing the API

The document is generated from the endpoints themselves (ASP.NET Core's OpenAPI support), so describing an endpoint is part of writing it:

- **Its group** has `.WithTags("...")`, and a new tag gets a sentence in `ApiReferenceText.Tags` ([`src/GarageStack.Api/Reference/ApiReferenceText.cs`](../src/GarageStack.Api/Reference/ApiReferenceText.cs)). A group that takes `{vin}` uses `.ResolvesVehicle()`, which resolves the vehicle and documents the `404` for an unknown VIN.
- **Every endpoint** has a `.WithSummary("...")`, and says what it answers: `.Produces<T>()` for a body, `.Produces(StatusCodes.Status204NoContent)` (or another status) without one, and `.ProducesProblem(status)` for each refusal it makes through `ApiProblems`. A handler returns `IResult`, so the framework cannot see this for itself.
- **Records** that go in or out carry XML doc comments: a `<summary>` and `<param>`s become the descriptions in the reference.
- **A body read by hand** (a `JsonElement`) is described with an operation transformer on the endpoint, as the command endpoint does with `VehicleCommandRequest`.

What every endpoint shares is added in one place, [`OpenApiSetup`](../src/GarageStack.Api/Reference/OpenApiSetup.cs): which ways in an endpoint accepts (from the token rules in `ApiTokenAuthentication.Refusal`, the same ones the API enforces), and the `401`, `403` and `429` any request can meet.

Two tests in `GarageStack.Tests` hold this up:

- `EveryEndpoint_DeclaresWhatItAnswers_AndItsGroupIsDescribed` fails for an endpoint without a summary, without a declared answer, or with a tag that has no description.
- `ApiDocument_MatchesTheCommittedCopy` compares the document with [`openapi.json`](openapi.json). When they differ, it writes the new document there and fails: look at the change, and commit it with the code that made it. The diff shows a reviewer what the API change looks like from the outside.

The page itself is [`ApiReferencePage`](../src/GarageStack.Api/Reference/ApiReferencePage.cs): where it is served, the sign-in redirect, and its Content-Security-Policy. `frontend/nginx-app.conf` gives `/api/reference` its own location without the app's policy, and `frontend/e2e/api-reference.spec.ts` checks the page in the production image.
