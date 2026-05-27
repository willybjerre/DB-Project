# MyWeatherApp

A .NET weather application backed by PostgreSQL (run via Docker).

## Getting started

To pull and run on another machine:

```bash
git clone https://github.com/willybjerre/DB-Project.git
cd DB-Project
docker compose -f Docker-Compose.yml up -d
dotnet ef database update
dotnet run
```