using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyWeatherApp.Data;
using MyWeatherApp.Services;

namespace MyWeatherApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeatherForecastController : ControllerBase
{
    private const int DefaultLocationId = 1;

    private readonly YrClient _yr;
    private readonly ForecastIngestionService _ingestion;
    private readonly ObservationIngestionService _observationIngestion;
    private readonly ComparisonService _comparison;
    private readonly WeatherDbContext _db;

    public WeatherForecastController(
        YrClient yr,
        ForecastIngestionService ingestion,
        ObservationIngestionService observationIngestion,
        ComparisonService comparison,
        WeatherDbContext db)
    {
        _yr = yr;
        _ingestion = ingestion;
        _observationIngestion = observationIngestion;
        _comparison = comparison;
        _db = db;
    }

    // Læse-only debug — kalder Yr uden at skrive til DB. Nyttig til at isolere
    // API-fejl fra DB-fejl. Fjernes når vi er trygge ved hele kæden.
    // GET api/weatherforecast/test/yr-fetch
    [HttpGet("test/yr-fetch")]
    public async Task<IActionResult> TestYrFetch(CancellationToken ct)
    {
        var points = await _yr.GetForecastAsync(55.6761, 12.5683, ct);
        return Ok(points);
    }

    // POST api/weatherforecast/ingest/yr
    [HttpPost("ingest/yr")]
    public async Task<IActionResult> IngestYr(CancellationToken ct)
    {
        try
        {
            var saved = await _ingestion.IngestYrForecastAsync(DefaultLocationId, ct);
            return Ok(new { saved });
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: 500, title: "Yr ingestion failed");
        }
    }

    // POST api/weatherforecast/ingest/dmi
    [HttpPost("ingest/dmi")]
    public async Task<IActionResult> IngestDmi(CancellationToken ct)
    {
        try
        {
            var saved = await _ingestion.IngestDmiForecastAsync(DefaultLocationId, ct);
            return Ok(new { saved });
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: 500, title: "DMI ingestion failed");
        }
    }

    // POST api/weatherforecast/ingest/observations
    [HttpPost("ingest/observations")]
    public async Task<IActionResult> IngestObservations(CancellationToken ct)
    {
        try
        {
            var saved = await _observationIngestion.IngestObservationsAsync(DefaultLocationId, ct);
            return Ok(new { saved });
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: 500, title: "Observation ingestion failed");
        }
    }

    // GET api/weatherforecast/comparison?days=30
    [HttpGet("comparison")]
    public async Task<IActionResult> GetComparison([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var result = await _comparison.GetComparisonAsync(DefaultLocationId, days, ct);
        return Ok(result);
    }

    // GET api/weatherforecast/timeseries?days=7
    [HttpGet("timeseries")]
    public async Task<IActionResult> GetTimeseries([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var result = await _comparison.GetTimeseriesAsync(DefaultLocationId, days, ct);
        return Ok(result);
    }

    // GET api/weatherforecast/tomorrow
    [HttpGet("tomorrow")]
    public async Task<IActionResult> GetTomorrow(CancellationToken ct = default)
    {
        // I morgen i UTC: [midnight tomorrow, midnight day-after).
        var tomorrowStart = DateTime.UtcNow.Date.AddDays(1);
        var tomorrowEnd = tomorrowStart.AddDays(1);

        var data = await _db.Forecasts
            .Where(f => f.LocationId == DefaultLocationId
                        && f.TargetDateTime >= tomorrowStart
                        && f.TargetDateTime < tomorrowEnd)
            .OrderBy(f => f.TargetDateTime)
            .ThenBy(f => f.Application.Name)
            .Select(f => new
            {
                f.TargetDateTime,
                Provider = f.Application.Name,
                f.PredTemp
            })
            .ToListAsync(ct);

        return Ok(data);
    }

    // GET api/weatherforecast/observations?days=30
    [HttpGet("observations")]
    public async Task<IActionResult> GetObservations([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-days);

        var data = await _db.Observations
            .Where(o => o.LocationId == DefaultLocationId
                        && o.ObsAt >= windowStart
                        && o.ObsAt <= now)
            .OrderBy(o => o.ObsAt)
            .Select(o => new { o.ObsAt, o.Temp })
            .ToListAsync(ct);

        return Ok(data);
    }
}
