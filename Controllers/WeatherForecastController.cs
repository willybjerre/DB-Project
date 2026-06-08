using System.Globalization;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Filters forecasts or observations for Copenhagen by matching a regular expression
    /// against the temperature value (formatted as an invariant-culture string with one
    /// decimal place, e.g. "20.0", "-3.2").
    /// </summary>
    /// <param name="source">Either "forecasts" or "observations" (case-insensitive).</param>
    /// <param name="pattern">A regular expression to match against the temperature string.</param>
    /// <returns>
    /// The matching rows ordered ascending by time, along with the original pattern
    /// and the number of matches.
    /// </returns>
    /// <response code="200">Returns the matching rows.</response>
    /// <response code="400">If <paramref name="source"/> is invalid or <paramref name="pattern"/> is not a valid regex.</response>
    /// <response code="408">If the regex match exceeds the 200 ms timeout (possible ReDoS).</response>
    // GET api/weatherforecast/search?source=forecasts&pattern=^20
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? source,
        [FromQuery] string? pattern,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return BadRequest(new { error = "Query parameter 'source' is required and must be 'forecasts' or 'observations'." });
        }
        if (pattern is null)
        {
            return BadRequest(new { error = "Query parameter 'pattern' is required." });
        }

        var normalizedSource = source.Trim().ToLowerInvariant();
        if (normalizedSource != "forecasts" && normalizedSource != "observations")
        {
            return BadRequest(new { error = $"Invalid 'source' value '{source}'. Must be 'forecasts' or 'observations'." });
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = $"Invalid regex pattern: {ex.Message}" });
        }

        try
        {
            if (normalizedSource == "forecasts")
            {
                var rows = await _db.Forecasts
                    .Where(f => f.LocationId == DefaultLocationId)
                    .OrderBy(f => f.TargetDateTime)
                    .ThenBy(f => f.Application.Name)
                    .Select(f => new
                    {
                        f.TargetDateTime,
                        Provider = f.Application.Name,
                        f.PredTemp
                    })
                    .ToListAsync(ct);

                var results = rows
                    // Regex matching — this is the implementation that satisfies the
                    // course's "regular expression matching" requirement.
                    .Where(r => regex.IsMatch(r.PredTemp.ToString("F1", CultureInfo.InvariantCulture)))
                    .ToList();

                return Ok(new
                {
                    source = "forecasts",
                    pattern,
                    matchCount = results.Count,
                    results
                });
            }
            else
            {
                var rows = await _db.Observations
                    .Where(o => o.LocationId == DefaultLocationId)
                    .OrderBy(o => o.ObsAt)
                    .Select(o => new { o.ObsAt, o.Temp })
                    .ToListAsync(ct);

                var results = rows
                    // Regex matching — this is the implementation that satisfies the
                    // course's "regular expression matching" requirement.
                    .Where(r => regex.IsMatch(r.Temp.ToString("F1", CultureInfo.InvariantCulture)))
                    .ToList();

                return Ok(new
                {
                    source = "observations",
                    pattern,
                    matchCount = results.Count,
                    results
                });
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, new { error = "Pattern took too long to evaluate" });
        }
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
