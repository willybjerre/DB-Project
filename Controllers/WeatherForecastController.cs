using Microsoft.AspNetCore.Mvc;
using MyWeatherApp.Services;

namespace MyWeatherApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly YrClient _yr;
    private readonly ForecastIngestionService _ingestion;

    public WeatherForecastController(YrClient yr, ForecastIngestionService ingestion)
    {
        _yr = yr;
        _ingestion = ingestion;
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
            var saved = await _ingestion.IngestYrForecastAsync(locationId: 1, ct);
            return Ok(new { saved });
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: 500, title: "Yr ingestion failed");
        }
    }

    // GET api/weatherforecast/forecast/30days
    [HttpGet("forecast/30DaysPercent")]
    public IActionResult Get30DaysPercent_Forcast_vs_Obs()
    {
        // TODO: hent fra database — alle forecasts fra de sidste 30 dage compare med observation.
        var data = Enumerable.Range(0, 30).Select(i => new
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i)),
            Provider = i % 2 == 0 ? "Yr" : "DMI",
            PredictedTempC = Random.Shared.Next(-5, 25)
        });

        return Ok(data);
    }

    // GET api/weatherforecast/forecast/tomorrow
    [HttpGet("forecast/tomorrow")]
    public IActionResult GetTomorrowForecast()
    {
        // TODO: hent dagens prognoser for i morgen kl. 10 UTC fra DB
        var tomorrowAt10 = DateTime.UtcNow.Date.AddDays(1).AddHours(10);

        var data = new[]
        {
            new { Provider = "Yr",  TargetTime = tomorrowAt10, PredictedTempC = 12.5 },
            new { Provider = "DMI", TargetTime = tomorrowAt10, PredictedTempC = 11.8 }
        };

        return Ok(data);
    }

    // GET api/weatherforecast/observations/30days
    [HttpGet("observations/30days")]
    public IActionResult Get30DaysObservations()
    {
        // TODO: hent observationer fra Meteostat-tabellen i DB
        var data = Enumerable.Range(0, 30).Select(i => new
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i)),
            ActualTempC = Random.Shared.Next(-5, 25)
        });

        return Ok(data);
    }
}