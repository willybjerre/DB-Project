# MyWeatherApp — HTTP API Reference

Reference for the frontend. Endpoints are documented from the actual code in
`Controllers/WeatherForecastController.cs` and the response DTOs in
`Services/ComparisonService.cs`.

## Base URL

- **Local dev:** `http://localhost:5070`
- **Deployed:** will differ — replace the host when we have a deploy target.
- All endpoints below are relative to that base.

All routes live under `/api/WeatherForecast/...`. ASP.NET route matching is
case-insensitive, so `/api/weatherforecast/...` also works; we'll use the
PascalCase form here to match what Swagger shows.

## Conventions

- JSON property names are **camelCase** (ASP.NET Core default).
- All `time` / `targetDateTime` / `obsAt` / `retrievedAt` values are **UTC**,
  ISO 8601 with a trailing `Z` (e.g. `"2026-05-20T10:00:00Z"`).
- `date` fields use `DateOnly` and serialize as `"YYYY-MM-DD"` strings.
- The default location is hard-coded to `LocationId = 1` (Copenhagen) for every
  endpoint. Multi-location support isn't wired up yet.
- Read endpoints return `200 OK` with a JSON body. Ingestion endpoints return
  `200 OK` with `{ "saved": <int> }` on success, and a standard ASP.NET
  `ProblemDetails` JSON body with `500` on failure.

## CORS — TODO

The frontend is not built yet, and **CORS is not configured on the backend**.
Once the frontend exists (probably a separate origin, e.g. a Vite dev server
on `localhost:5173`), CORS will need to be turned on in `Program.cs` —
something like `builder.Services.AddCors(...)` + `app.UseCors(...)` with the
frontend origin in the allow-list. Until that's done, browser fetches from a
different origin will be blocked.

---

## 1. Read endpoints (for the frontend)

All four read from the database only.

### GET `/api/WeatherForecast/comparison`

Per-day and overall mean absolute error (MAE, °C) for each forecast provider
versus observed temperatures, over the last N days.

**Query parameters**

| Name | Type | Default | Notes |
|------|------|---------|-------|
| `days` | int | `30` | Window size. Typical values 7 or 30. |

**Response example**

```json
{
  "days": 30,
  "perDay": [
    { "date": "2026-05-20", "provider": "DMI", "hoursMatched": 8, "mae": 1.11 },
    { "date": "2026-05-20", "provider": "Yr",  "hoursMatched": 8, "mae": 1.42 }
  ],
  "summary": [
    { "provider": "DMI", "totalHoursMatched": 140, "overallMae": 1.05 },
    { "provider": "Yr",  "totalHoursMatched": 140, "overallMae": 1.38 }
  ],
  "mostAccurate": "DMI"
}
```

**Nullables / empty case**

- `mostAccurate` is `string | null`. It's `null` when there's no overlapping
  forecast/observation data in the window.
- When there's no data at all, `perDay` and `summary` are returned as `[]` and
  `mostAccurate` as `null` — the endpoint does not throw.
- `mae` values are rounded to 2 decimals.
- A day appears in `perDay` only if at least one forecast hour for that day
  also has a matching observation. `hoursMatched` shows how complete the day
  is (max 24).

### GET `/api/WeatherForecast/timeseries`

Raw merged time series of Yr forecast, DMI forecast, and observed temperature
on a single time axis — for charting three lines. No calculation, just a
fetch + merge.

**Query parameters**

| Name | Type | Default | Notes |
|------|------|---------|-------|
| `days` | int | `7` | Window size. Typical values 7 or 30. |

**Response example**

```json
{
  "days": 7,
  "points": [
    { "time": "2026-05-20T09:00:00Z", "yr": 14.2, "dmi": 13.8, "observed": 13.5 },
    { "time": "2026-05-20T10:00:00Z", "yr": 14.5, "dmi": null, "observed": 13.9 },
    { "time": "2026-05-20T11:00:00Z", "yr": null, "dmi": null, "observed": 14.1 }
  ]
}
```

**Nullables / empty case**

- `yr`, `dmi`, `observed` are each `number | null`. A field is `null` when
  that source has no value at that exact UTC timestamp.
- A point is included if **any** of the three sources has a value for the
  timestamp; partial points are not dropped.
- Temperature values are rounded to 1 decimal.
- Points are ordered ascending by `time`.
- If no data exists in the window, `points` is `[]`.

### GET `/api/WeatherForecast/tomorrow`

All stored forecasts for tomorrow (UTC), both providers. Lookup only — no
calculation.

**Query parameters**

None.

**Response example**

```json
[
  { "targetDateTime": "2026-05-28T00:00:00Z", "provider": "DMI", "predTemp": 11.8 },
  { "targetDateTime": "2026-05-28T00:00:00Z", "provider": "Yr",  "predTemp": 12.1 },
  { "targetDateTime": "2026-05-28T01:00:00Z", "provider": "DMI", "predTemp": 11.5 },
  { "targetDateTime": "2026-05-28T01:00:00Z", "provider": "Yr",  "predTemp": 11.9 }
]
```

**Nullables / empty case**

- Top-level response is a JSON array (no envelope).
- All fields are always present.
- Ordered by `targetDateTime` ascending, then `provider` ascending.
- Window is `[midnight tomorrow UTC, midnight day-after UTC)`.
- Empty array if neither provider has been ingested with forecasts for
  tomorrow yet.

### GET `/api/WeatherForecast/observations`

All stored ground-truth observations for the last N days. Lookup only.

**Query parameters**

| Name | Type | Default | Notes |
|------|------|---------|-------|
| `days` | int | `30` | Window size. |

**Response example**

```json
[
  { "obsAt": "2026-05-20T09:00:00Z", "temp": 13.5 },
  { "obsAt": "2026-05-20T10:00:00Z", "temp": 13.9 }
]
```

**Nullables / empty case**

- Top-level response is a JSON array.
- All fields are always present (rows with `null` temp are filtered out at
  ingest time by `MeteostatClient`, so they never reach the DB).
- Ordered by `obsAt` ascending.
- Empty array if no observations have been ingested in the window.

---

## 2. Ingestion endpoints (POST — manual triggers)

These fetch from external providers and write to the database. They duplicate
what the `DailyIngestionService` does on a schedule (05:00 UTC for forecasts,
13:00 UTC for observations), and exist for debugging and on-demand runs. The
frontend shouldn't need to call these.

All three return `{ "saved": <int> }` on success and a `ProblemDetails` body
with HTTP 500 on failure.

### POST `/api/WeatherForecast/ingest/yr`

Fetches the next 24 hours of Yr (MET Norway) forecasts for `LocationId = 1`
and upserts into `Forecasts`. Returns the number of rows processed
(inserted or updated).

**Success response**

```json
{ "saved": 24 }
```

**Failure response (500)**

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "Yr ingestion failed",
  "status": 500,
  "detail": "<exception message>"
}
```

### POST `/api/WeatherForecast/ingest/dmi`

Same as the Yr endpoint but fetches DMI's `dmi_seamless` model via Open-Meteo.
Failure `title` is `"DMI ingestion failed"`.

```json
{ "saved": 24 }
```

### POST `/api/WeatherForecast/ingest/observations`

Fetches yesterday + today's hourly observations from Meteostat (RapidAPI) for
the location's `ObservationStationId` and upserts into `Observations`. Skips
rows where Meteostat returns a null temperature. Failure `title` is
`"Observation ingestion failed"`.

```json
{ "saved": 48 }
```

> Requires `Meteostat:ApiKey` in user-secrets — if missing, the 500 response's
> `detail` will say so explicitly.

---

## 3. Debug endpoints

### GET `/api/WeatherForecast/test/yr-fetch`

Read-only debug endpoint. Calls Yr directly for Copenhagen (lat 55.6761,
lon 12.5683) and returns the parsed forecast points. **Does not write to the
DB.** Useful for isolating an external-API failure from a DB failure.

**Response example**

```json
[
  { "targetUtc": "2026-05-27T13:00:00Z", "tempC": 15.2 },
  { "targetUtc": "2026-05-27T14:00:00Z", "tempC": 15.4 }
]
```

This endpoint has a comment in the code saying it will be removed once the
ingestion chain is fully trusted — so don't build the frontend against it.
