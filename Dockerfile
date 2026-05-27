# syntax=docker/dockerfile:1.7

# ----- Build stage -----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore som eget lag — undgår restore på hver kode-ændring.
COPY MyWeatherApp.csproj ./
RUN dotnet restore MyWeatherApp.csproj

# Kopier resten og publicer i Release.
COPY . .
RUN dotnet publish MyWeatherApp.csproj -c Release -o /app/publish /p:UseAppHost=false

# ----- Runtime stage -----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Railway sætter PORT på runtime — bind Kestrel til 0.0.0.0:$PORT så proxyen
# kan nå app'en. Localhost-bind ville være usynligt udefra. Fallback 8080 for
# lokal `docker run` uden PORT sat. `exec` så SIGTERM rammer dotnet direkte
# og containeren stopper rent.
ENTRYPOINT ["sh", "-c", "exec dotnet MyWeatherApp.dll --urls http://0.0.0.0:${PORT:-8080}"]
