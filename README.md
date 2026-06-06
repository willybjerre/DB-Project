# Forecast Accuracy — Yr vs DMI

University of Copenhagen, DIKU — Databases and Information Systems, spring 2026.

A .NET 9 Web API with PostgreSQL that compares the forecast accuracy of two weather services, **Yr** (MET Norway) and **DMI** (Danmarks Meteorologiske Institut, via Open-Meteo), for Copenhagen. The system fetches forecasts and observed temperatures daily, stores everything, and computes mean absolute error (MAE) between each provider's predictions and the actual temperature measured at Copenhagen Airport (Meteostat station 06180).

Since the application requires data collected per day it needs to run for a while to have a efficient purpose, this is done via background service in the c# backend, running locally is therefor not recommended.

## Getting started

To pull and run on another machine:

```bash
git clone https://github.com/willybjerre/DB-Project.git
cd DB-Project
docker compose -f Docker-Compose.yml up -d
dotnet ef database update
dotnet run
```

## Live system

- **Backend:** https://db-project-production-0e3d.up.railway.app
- **Frontend:** https://willybjerre.github.io/WeatherAPP_Frontend/
- **repo:** https://github.com/willybjerre/WeatherAPP_Frontend && https://github.com/willybjerre/DB-Project/


## Run locally

Requires Docker, .NET 9 SDK, and a free Meteostat API key (sign up on RapidAPI → subscribe to Meteostat's free tier).

```bash
# 1. Start Postgres
docker compose -f Docker-Compose.yml up -d

# 2. Set the Meteostat API key (once per machine)
dotnet user-secrets set "Meteostat:ApiKey" "<your key>"

# 3. Run the app
dotnet run
```
the system runs on a server and needs to be live for a while to have accurate data which means when you run it locally it wont have enough data yet to be accurate.

If you still want to run it, then dotnet run the backend, go into the frontend folder and write "python3 -m http.server 5173" and follow the instructions above.

## Tech stack

- .NET 9 Web API
- Entity Framework Core 9 with Npgsql
- PostgreSQL 16 (Docker)
- Chart.js (frontend)
- Deployed on Railway (backend) and GitHub Pages (frontend)
