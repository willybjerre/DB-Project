using Microsoft.EntityFrameworkCore;
using MyWeatherApp.Data;
using MyWeatherApp.Models;

namespace MyWeatherApp.Services;

public class ObservationIngestionService
{
    private readonly WeatherDbContext _db;
    private readonly MeteostatClient _meteostat;
    private readonly ILogger<ObservationIngestionService> _log;

    public ObservationIngestionService(
        WeatherDbContext db,
        MeteostatClient meteostat,
        ILogger<ObservationIngestionService> log)
    {
        _db = db;
        _meteostat = meteostat;
        _log = log;
    }

    public async Task<int> IngestObservationsAsync(int locationId, CancellationToken ct = default)
    {
        var location = await _db.Locations.FindAsync([locationId], ct)
            ?? throw new InvalidOperationException($"Location {locationId} not found.");

        if (string.IsNullOrWhiteSpace(location.ObservationStationId))
        {
            throw new InvalidOperationException(
                $"Location {locationId} has no ObservationStationId.");
        }

        // Hent i går + i dag som hele datoer. Meteostat tager kun YYYY-MM-DD,
        // så det er den enkleste måde at dække de seneste ~24 timer på.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        var points = await _meteostat.GetObservationsAsync(
            location.ObservationStationId, yesterday, today, ct);

        if (points.Count == 0)
        {
            _log.LogWarning(
                "Meteostat returned 0 usable observations for location {LocationId} (station {Station})",
                locationId, location.ObservationStationId);
            return 0;
        }

        // Hent eksisterende rækker i ét kald, så vi kan upserte uden separate queries per række.
        var minTime = points.Min(p => p.ObservedUtc);
        var maxTime = points.Max(p => p.ObservedUtc);

        var existing = await _db.Observations
            .Where(o => o.LocationId == locationId
                        && o.ObsAt >= minTime
                        && o.ObsAt <= maxTime)
            .ToDictionaryAsync(o => o.ObsAt, ct);

        var processed = 0;
        foreach (var p in points)
        {
            if (existing.TryGetValue(p.ObservedUtc, out var row))
            {
                row.Temp = p.TempC;
            }
            else
            {
                _db.Observations.Add(new Observation
                {
                    LocationId = locationId,
                    ObsAt = p.ObservedUtc,
                    Temp = p.TempC
                });
            }
            processed++;
        }

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Ingested {Count} Meteostat observations for location {LocationId}",
            processed, locationId);

        return processed;
    }
}
