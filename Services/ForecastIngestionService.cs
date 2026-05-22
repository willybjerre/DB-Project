using Microsoft.EntityFrameworkCore;
using MyWeatherApp.Data;
using MyWeatherApp.Models;

namespace MyWeatherApp.Services;

public class ForecastIngestionService
{
    private readonly WeatherDbContext _db;
    private readonly YrClient _yr;
    private readonly ILogger<ForecastIngestionService> _log;

    public ForecastIngestionService(
        WeatherDbContext db,
        YrClient yr,
        ILogger<ForecastIngestionService> log)
    {
        _db = db;
        _yr = yr;
        _log = log;
    }

    public async Task<int> IngestYrForecastAsync(int locationId, CancellationToken ct = default)
    {
        var location = await _db.Locations.FindAsync([locationId], ct)
            ?? throw new InvalidOperationException($"Location {locationId} not found.");

        var yrApp = await _db.Applications.FirstOrDefaultAsync(a => a.Name == "Yr", ct)
            ?? throw new InvalidOperationException("Application 'Yr' is not seeded.");

        var points = await _yr.GetForecastAsync(location.Latitude, location.Longitude, ct);

        if (points.Count < 12)
        {
            _log.LogWarning(
                "Yr returned only {Count} forecasts for location {LocationId} (sanity threshold is 12)",
                points.Count, locationId);
        }

        if (points.Count == 0)
        {
            return 0;
        }

        // Hent eksisterende rækker i ét kald, så vi kan upserte uden 24 separate queries.
        var minTime = points.Min(p => p.TargetUtc);
        var maxTime = points.Max(p => p.TargetUtc);

        var existing = await _db.Forecasts
            .Where(f => f.AppId == yrApp.AppId
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
                    AppId = yrApp.AppId,
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
            "Ingested {Count} Yr forecasts for location {LocationId}",
            processed, locationId);

        return processed;
    }
}
