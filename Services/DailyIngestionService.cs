namespace MyWeatherApp.Services;

public class DailyIngestionService : BackgroundService
{
    private const int DefaultLocationId = 1;
    private const int ForecastFetchHourUtc = 5;
    private const int ObservationFetchHourUtc = 13;
    private static readonly TimeSpan MaxForecastJitter = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyIngestionService> _log;

    // Pr. opgave: datoen for seneste kørsel. Hour-gaten alene ville være nok
    // mod gentagne kørsler, men hvis timeren drifter og fyrer to gange inden
    // for samme time, sikrer date-trackeren at vi ikke ingester dobbelt.
    private DateOnly? _lastForecastRun;
    private DateOnly? _lastObservationRun;

    public DailyIngestionService(
        IServiceScopeFactory scopeFactory,
        ILogger<DailyIngestionService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "DailyIngestionService started. Forecast fetch at {ForecastHour}:00 UTC, observation fetch at {ObsHour}:00 UTC.",
            ForecastFetchHourUtc, ObservationFetchHourUtc);

        // Kør tjekket med det samme — så hvis service starter f.eks. 05:30 UTC
        // får vi stadig kørt dagens forecast-fetch i stedet for at vente til næste dag.
        await CheckAndRunAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckAndRunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — ingen logning ud over stopping-beskeden nedenfor.
        }

        _log.LogInformation("DailyIngestionService stopping.");
    }

    private async Task CheckAndRunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        if (now.Hour == ForecastFetchHourUtc && _lastForecastRun != today)
        {
            await RunForecastFetchAsync(ct);
            _lastForecastRun = today;
        }

        if (now.Hour == ObservationFetchHourUtc && _lastObservationRun != today)
        {
            await RunObservationFetchAsync(ct);
            _lastObservationRun = today;
        }
    }

    private async Task RunForecastFetchAsync(CancellationToken ct)
    {
        // MET Norways ToS beder klienter undgå at ramme præcis på timen — random
        // jitter 0-10 minutter spreder forespørgsler ud og respekterer det.
        var jitter = TimeSpan.FromSeconds(Random.Shared.Next(0, (int)MaxForecastJitter.TotalSeconds));
        _log.LogInformation("Forecast fetch starting after {Jitter} jitter delay.", jitter);

        try
        {
            await Task.Delay(jitter, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // BackgroundService er singleton, ForecastIngestionService er scoped
        // (DbContext er scoped). Vi laver derfor en ny scope pr. kørsel.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var forecasts = scope.ServiceProvider.GetRequiredService<ForecastIngestionService>();

        try
        {
            var yrCount = await forecasts.IngestYrForecastAsync(DefaultLocationId, ct);
            _log.LogInformation("Daily Yr forecast ingestion finished: {Count} rows.", yrCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Daily Yr forecast ingestion failed.");
        }

        try
        {
            var dmiCount = await forecasts.IngestDmiForecastAsync(DefaultLocationId, ct);
            _log.LogInformation("Daily DMI forecast ingestion finished: {Count} rows.", dmiCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Daily DMI forecast ingestion failed.");
        }
    }

    private async Task RunObservationFetchAsync(CancellationToken ct)
    {
        _log.LogInformation("Observation fetch starting.");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var observations = scope.ServiceProvider.GetRequiredService<ObservationIngestionService>();

        try
        {
            var count = await observations.IngestObservationsAsync(DefaultLocationId, ct);
            _log.LogInformation("Daily Meteostat observation ingestion finished: {Count} rows.", count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Daily Meteostat observation ingestion failed.");
        }
    }
}
