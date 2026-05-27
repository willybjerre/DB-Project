using Microsoft.EntityFrameworkCore;
using MyWeatherApp.Data;
using MyWeatherApp.Models;

namespace MyWeatherApp.Services;

public class ForecastIngestionService
{
    private readonly WeatherDbContext _db;
    private readonly YrClient _yr;
    private readonly DmiClient _dmi;
    private readonly ILogger<ForecastIngestionService> _log;

    public ForecastIngestionService(
        WeatherDbContext db,
        YrClient yr,
        DmiClient dmi,
        ILogger<ForecastIngestionService> log)
    {
        _db = db;
        _yr = yr;
        _dmi = dmi;
        _log = log;
    }

    public async Task<int> IngestYrForecastAsync(int locationId, CancellationToken ct = default)
    {
        var location = await _db.Locations.FindAsync([locationId], ct)
            ?? throw new InvalidOperationException($"Location {locationId} not found.");

        var points = await _yr.GetForecastAsync(location.Latitude, location.Longitude, ct);

        return await UpsertForecastsAsync("Yr", locationId, points, ct);
    }

    public async Task<int> IngestDmiForecastAsync(int locationId, CancellationToken ct = default)
    {
        var location = await _db.Locations.FindAsync([locationId], ct)
            ?? throw new InvalidOperationException($"Location {locationId} not found.");

        var points = await _dmi.GetForecastAsync(location.Latitude, location.Longitude, ct);

        return await UpsertForecastsAsync("DMI", locationId, points, ct);
    }

    private async Task<int> UpsertForecastsAsync(
        string providerName,
        int locationId,
        IReadOnlyList<ForecastPoint> points,
        CancellationToken ct)
    {
        var app = await _db.Applications.FirstOrDefaultAsync(a => a.Name == providerName, ct)
            ?? throw new InvalidOperationException($"Application '{providerName}' is not seeded.");

        if (points.Count < 12)
        {
            _log.LogWarning(
                "{Provider} returned only {Count} forecasts for location {LocationId} (sanity threshold is 12)",
                providerName, points.Count, locationId);
        }

        if (points.Count == 0)
        {
            return 0;
        }

        // Hent eksisterende rækker i ét kald, så vi kan upserte uden 24 separate queries.
        var minTime = points.Min(p => p.TargetUtc);
        var maxTime = points.Max(p => p.TargetUtc);

        var existing = await _db.Forecasts
            .Where(f => f.AppId == app.AppId
                        && f.LocationId == locationId
                        && f.TargetDateTime >= minTime
                        && f.TargetDateTime <= maxTime)
            .ToDictionaryAsync(f => f.TargetDateTime, ct);

        var now = DateTime.UtcNow;
        var processed = 0;

        foreach (var p in points)
        {
            if (existing.TryGetValue(p.TargetUtc, out var row))
            {
                row.PredTemp = p.TempC;
                row.RetrievedAt = now;
            }
            else
            {
                _db.Forecasts.Add(new Forecast
                {
                    AppId = app.AppId,
                    LocationId = locationId,
                    TargetDateTime = p.TargetUtc,
                    PredTemp = p.TempC,
                    RetrievedAt = now
                });
            }
            processed++;
        }

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Ingested {Count} {Provider} forecasts for location {LocationId}",
            processed, providerName, locationId);

        return processed;
    }
}
