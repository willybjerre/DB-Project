using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace MyWeatherApp.Services;

public record ObservationPoint(DateTime ObservedUtc, double TempC);

public class MeteostatClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MeteostatClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public MeteostatClient(HttpClient http, IConfiguration config, ILogger<MeteostatClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ObservationPoint>> GetObservationsAsync(
        string stationId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        // RapidAPI-nøglen ligger i user-secrets — sættes med:
        // dotnet user-secrets set "Meteostat:ApiKey" "<din-nøgle>"
        var apiKey = _config["Meteostat:ApiKey"]
            ?? throw new InvalidOperationException(
                "Meteostat API key missing. Set it with: dotnet user-secrets set \"Meteostat:ApiKey\" \"<your-key>\"");

        var url = string.Format(
            CultureInfo.InvariantCulture,
            "stations/hourly?station={0}&start={1}&end={2}",
            stationId,
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        _logger.LogInformation("Meteostat request → {Url}", url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-rapidapi-host", "meteostat.p.rapidapi.com");
        request.Headers.Add("x-rapidapi-key", apiKey);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MeteostatResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Meteostat returned an empty response.");

        var result = new List<ObservationPoint>(payload.Data.Count);
        var skipped = 0;

        foreach (var row in payload.Data)
        {
            // Meteostat kan returnere null temp når stationen har et hul i målingerne.
            // Vi springer dem over fremfor at fylde DB med 0-værdier eller smide exceptions.
            if (row.Temp is null)
            {
                skipped++;
                continue;
            }

            // "time" kommer som "YYYY-MM-DD HH:MM:SS" uden zone — den er UTC pr. API-aftale.
            // ParseExact + AssumeUniversal/AdjustToUniversal giver os en DateTime med Kind=Utc,
            // som Npgsql kræver for at skrive til en timestamptz-kolonne.
            var observedUtc = DateTime.ParseExact(
                row.Time,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            result.Add(new ObservationPoint(observedUtc, row.Temp.Value));
        }

        _logger.LogInformation(
            "Meteostat returned {Total} rows for station {Station} ({Kept} kept, {Skipped} skipped for null temp)",
            payload.Data.Count, stationId, result.Count, skipped);

        return result;
    }

    private sealed record MeteostatResponse(IReadOnlyList<MeteostatHourly> Data);
    private sealed record MeteostatHourly(string Time, double? Temp);
}
