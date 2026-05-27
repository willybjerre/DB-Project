using Microsoft.EntityFrameworkCore;
using MyWeatherApp.Data;

namespace MyWeatherApp.Services;

public record DailyAccuracy(DateOnly Date, string Provider, int HoursMatched, double Mae);
public record ProviderSummary(string Provider, int TotalHoursMatched, double OverallMae);
public record ComparisonResult(
    int Days,
    IReadOnlyList<DailyAccuracy> PerDay,
    IReadOnlyList<ProviderSummary> Summary,
    string? MostAccurate);

public record TimeseriesPoint(DateTime Time, double? Yr, double? Dmi, double? Observed);
public record TimeseriesResult(int Days, IReadOnlyList<TimeseriesPoint> Points);

public class ComparisonService
{
    private readonly WeatherDbContext _db;

    public ComparisonService(WeatherDbContext db)
    {
        _db = db;
    }

    public async Task<ComparisonResult> GetComparisonAsync(
        int locationId, int days, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-days);

        // Hent forecasts og observations separat — joinet laver vi i C# pr. opgavebeskrivelsen.
        // Application.Name kommer med via projection, så vi ikke skal slå AppId op senere.
        var forecasts = await _db.Forecasts
            .Where(f => f.LocationId == locationId
                        && f.TargetDateTime >= windowStart
                        && f.TargetDateTime <= now)
            .Select(f => new
            {
                Provider = f.Application.Name,
                f.TargetDateTime,
                f.PredTemp
            })
            .ToListAsync(ct);

        var observations = await _db.Observations
            .Where(o => o.LocationId == locationId
                        && o.ObsAt >= windowStart
                        && o.ObsAt <= now)
            .Select(o => new { o.ObsAt, o.Temp })
            .ToListAsync(ct);

        // Slå observationer op pr. tidspunkt så joinet er O(n) i antal forecasts
        // i stedet for O(n*m). LocationId er allerede filtreret væk i begge queries.
        var obsByTime = observations.ToDictionary(o => o.ObsAt, o => o.Temp);

        var matched = forecasts
            .Where(f => obsByTime.ContainsKey(f.TargetDateTime))
            .Select(f => new
            {
                f.Provider,
                f.TargetDateTime,
                AbsError = Math.Abs(f.PredTemp - obsByTime[f.TargetDateTime])
            })
            .ToList();

        if (matched.Count == 0)
        {
            return new ComparisonResult(days, [], [], null);
        }

        // Pr. dag (UTC) pr. provider: antal matchede timer + MAE for de timer.
        var perDay = matched
            .GroupBy(m => new
            {
                Date = DateOnly.FromDateTime(m.TargetDateTime),
                m.Provider
            })
            .Select(g => new DailyAccuracy(
                g.Key.Date,
                g.Key.Provider,
                g.Count(),
                Math.Round(g.Average(x => x.AbsError), 2)))
            .OrderBy(d => d.Date)
            .ThenBy(d => d.Provider)
            .ToList();

        // Samlet pr. provider over hele vinduet.
        var summary = matched
            .GroupBy(m => m.Provider)
            .Select(g => new ProviderSummary(
                g.Key,
                g.Count(),
                Math.Round(g.Average(x => x.AbsError), 2)))
            .OrderBy(s => s.Provider)
            .ToList();

        var mostAccurate = summary.OrderBy(s => s.OverallMae).First().Provider;

        return new ComparisonResult(days, perDay, summary, mostAccurate);
    }

    public async Task<TimeseriesResult> GetTimeseriesAsync(
        int locationId, int days, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-days);

        var forecasts = await _db.Forecasts
            .Where(f => f.LocationId == locationId
                        && f.TargetDateTime >= windowStart
                        && f.TargetDateTime <= now)
            .Select(f => new
            {
                Provider = f.Application.Name,
                f.TargetDateTime,
                f.PredTemp
            })
            .ToListAsync(ct);

        var observations = await _db.Observations
            .Where(o => o.LocationId == locationId
                        && o.ObsAt >= windowStart
                        && o.ObsAt <= now)
            .Select(o => new { o.ObsAt, o.Temp })
            .ToListAsync(ct);

        // Saml alle tre kilder i én dict keyed by timestamp. Hver slot har tre
        // nullable felter — vi udfylder kun det felt der svarer til kilden,
        // og lader resten være null hvis den kilde ikke har en værdi for tidspunktet.
        var byTime = new Dictionary<DateTime, (double? Yr, double? Dmi, double? Observed)>();

        foreach (var f in forecasts)
        {
            byTime.TryGetValue(f.TargetDateTime, out var slot);
            var rounded = Math.Round(f.PredTemp, 1);
            if (f.Provider == "Yr")
            {
                slot.Yr = rounded;
            }
            else if (f.Provider == "DMI")
            {
                slot.Dmi = rounded;
            }
            byTime[f.TargetDateTime] = slot;
        }

        foreach (var o in observations)
        {
            byTime.TryGetValue(o.ObsAt, out var slot);
            slot.Observed = Math.Round(o.Temp, 1);
            byTime[o.ObsAt] = slot;
        }

        var points = byTime
            .OrderBy(kv => kv.Key)
            .Select(kv => new TimeseriesPoint(kv.Key, kv.Value.Yr, kv.Value.Dmi, kv.Value.Observed))
            .ToList();

        return new TimeseriesResult(days, points);
    }
}
