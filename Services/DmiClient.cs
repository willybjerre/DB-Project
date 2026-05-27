using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyWeatherApp.Services;

public class DmiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DmiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public DmiClient(HttpClient http, ILogger<DmiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ForecastPoint>> GetForecastAsync(
        double lat, double lng, CancellationToken ct = default)
    {
        var url = string.Format(
            CultureInfo.InvariantCulture,
            "v1/forecast?latitude={0}&longitude={1}&hourly=temperature_2m&forecast_days=2&models=dmi_seamless",
            lat, lng);

        _logger.LogInformation("DMI (Open-Meteo) request → {Url}", url);

        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenMeteoResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Open-Meteo returned an empty response.");

        var times = payload.Hourly.Time;
        var temps = payload.Hourly.Temperature2m;

        if (times.Count != temps.Count)
        {
            throw new InvalidOperationException(
                $"Open-Meteo time/temperature arrays mismatched: {times.Count} vs {temps.Count}");
        }

        // Næste fulde time UTC og 24 timer frem (samme vindue som YrClient).
        var now = DateTime.UtcNow;
        var nextHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc)
            .AddHours(1);
        var endExclusive = nextHour.AddHours(24);

        var result = new List<ForecastPoint>(24);
        for (var i = 0; i < times.Count; i++)
        {
            // Open-Meteo returnerer timestamps uden zone-suffix — de er UTC pr. default.
            // AssumeUniversal+AdjustToUniversal giver os en DateTime med Kind=Utc, som Npgsql
            // kræver for at skrive til en timestamptz-kolonne.
            var t = DateTime.Parse(
                times[i],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            if (t >= nextHour && t < endExclusive)
            {
                result.Add(new ForecastPoint(t, temps[i]));
            }
        }

        return result;
    }

    private sealed record OpenMeteoResponse(OpenMeteoHourly Hourly);
    private sealed record OpenMeteoHourly(
        IReadOnlyList<string> Time,
        [property: JsonPropertyName("temperature_2m")] IReadOnlyList<double> Temperature2m);
}
