# Frontend

Plain HTML / JS / CSS. No build step, no npm. Chart.js is loaded from a CDN
inside `index.html`.

## Running

From inside this `frontend/` directory:

```
python3 -m http.server 5173
```

Then open <http://localhost:5173> in a browser.

## Requirements

The frontend needs the backend running on **http://localhost:5070**:

1. Start Postgres:
   ```
   docker compose -f Docker-Compose.yml up -d
   ```
   (run from the repo root)

2. Start the API:
   ```
   dotnet run
   ```
   (run from the repo root)

CORS for `http://localhost:5173` is already configured in the backend's
`Program.cs`, so this origin works out of the box. If you serve the frontend
from a different port, update the CORS policy in the backend too.

## Files

- `index.html` — markup: hero banner, three section cards (Verdict, Next 24
  hours, History), segmented 7d/30d toggle. Loads Chart.js from
  `cdn.jsdelivr.net`.
- `app.js` — fetch logic + Chart.js setup. Backend base URL lives in the
  `API_BASE` const at the top.
- `style.css` — dark dashboard theme. Accent colours: Yr orange `#f5a623`,
  DMI blue `#3b9eff`, Observed green `#3ecf8e` — used consistently in charts
  and verdict cards.
- `hero.jpg` — banner image at the top of the page (the "Yr vs DMI" storm
  giants illustration). Drop the file in this directory; if it's missing the
  banner area still renders (dark placeholder) but with the title overlay
  intact. If your file has a different name, update the `src` in
  `index.html` accordingly.

## Endpoints used

All routes are documented in `../API.md`. The page calls three of them:

- `GET /api/WeatherForecast/comparison?days={N}` — Verdict section.
- `GET /api/WeatherForecast/tomorrow` — Next 24 hours section.
- `GET /api/WeatherForecast/timeseries?days={N}` — History section.

The 7d/30d toggle re-fetches `comparison` and `timeseries` only; `tomorrow`
is unaffected.
