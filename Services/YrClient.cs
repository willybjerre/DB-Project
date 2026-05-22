using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyWeatherApp.Services;

public record ForecastPoint(DateTime TargetUtc, double TempC);

public class YrClient
{
    private readonly HttpClient _http;
    private readonly ILogger<YrClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public YrClient(HttpClient http, ILogger<YrClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ForecastPoint>> GetForecastAsync(
        double lat, double lng, CancellationToken ct = default)
    {
        var url = string.Format(
            CultureInfo.InvariantCulture,
            "weatherapi/locationforecast/2.0/compact?lat={0}&lon={1}",
            lat, lng);

        var userAgent = _http.DefaultRequestHeaders.UserAgent.ToString();
        _logger.LogInformation("Yr request → {Url} with User-Agent: {UserAgent}", url, userAgent);

        using var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException(
                $"Yr returned 403 — usually a User-Agent issue. Sent: {userAgent}");
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<YrResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Yr returned an empty response.");

        // Næste fulde time UTC og 24 timer frem
        var now = DateTime.UtcNow;
        var nextHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc)
            .AddHours(1);
        var endExclusive = nextHour.AddHours(24);

        return payload.Properties.Timeseries
            .Where(ts => ts.Time >= nextHour && ts.Time < endExclusive)
            .Select(ts => new ForecastPoint(ts.Time, ts.Data.Instant.Details.AirTemperature))
            .ToList();
    }

    private sealed record YrResponse(YrProperties Properties);
    private sealed record YrProperties(IReadOnlyList<YrTimeseriesEntry> Timeseries);
    private sealed record YrTimeseriesEntry(DateTime Time, YrEntryData Data);
    private sealed record YrEntryData(YrInstant Instant);
    private sealed record YrInstant(YrDetails Details);
    private sealed record YrDetails([property: JsonPropertyName("air_temperature")] double AirTemperature);
}
