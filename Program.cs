using Microsoft.EntityFrameworkCore;
using MyWeatherApp.Data;
using MyWeatherApp.Models;
using MyWeatherApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<WeatherDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WeatherDb")));

builder.Services.AddHttpClient<YrClient>(c =>
{
    c.BaseAddress = new Uri("https://api.met.no/");
    // Bypass C#'s strict header validation — RFC 7231 ville kræve parenteser om e-mailen
    // som "comment", men curl bekræftede at MET Norway accepterer det rå format uden.
    c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DIKUWeatherProject/0.1 willybjerre@gmail.com");
});

builder.Services.AddHttpClient<DmiClient>(c =>
{
    c.BaseAddress = new Uri("https://api.open-meteo.com/");
});

builder.Services.AddHttpClient<MeteostatClient>(c =>
{
    c.BaseAddress = new Uri("https://meteostat.p.rapidapi.com/");
});

builder.Services.AddScoped<ForecastIngestionService>();
builder.Services.AddScoped<ObservationIngestionService>();
builder.Services.AddScoped<ComparisonService>();

builder.Services.AddHostedService<DailyIngestionService>();

// Allowed origins kommer fra config "Cors:AllowedOrigins" (komma-separeret).
// På Railway sættes den via env-var Cors__AllowedOrigins. Fallback til localhost:5173
// så lokal udvikling virker uden config.
var corsOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Migrer skemaet og seed reference-data ved opstart. Det betyder at en frisk
// Railway-Postgres bare virker — ingen manuel `dotnet ef database update` eller
// db/seed.sql. Idempotent: gentagne kørsler tilføjer ikke duplikater.
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var logger = sp.GetRequiredService<ILogger<Program>>();

    try
    {
        var db = sp.GetRequiredService<WeatherDbContext>();

        logger.LogInformation("Applying database migrations...");
        db.Database.Migrate();

        var added = 0;

        if (!db.Applications.Any(a => a.Name == "Yr"))
        {
            db.Applications.Add(new Application { Name = "Yr" });
            added++;
        }
        if (!db.Applications.Any(a => a.Name == "DMI"))
        {
            db.Applications.Add(new Application { Name = "DMI" });
            added++;
        }
        if (!db.Locations.Any(l => l.Name == "Copenhagen"))
        {
            db.Locations.Add(new Location
            {
                Name = "Copenhagen",
                Latitude = 55.6761,
                Longitude = 12.5683,
                ObservationStationId = "06180"
            });
            added++;
        }

        if (added > 0)
        {
            db.SaveChanges();
            logger.LogInformation("Seeded {Count} missing reference rows.", added);
        }
        else
        {
            logger.LogInformation("Reference data already present — nothing to seed.");
        }
    }
    catch (Exception ex)
    {
        // Re-throw så containeren stopper rent og Railway viser fejlen — bedre
        // end at starte op med en halv-konfigureret DB hvor alle requests fejler.
        logger.LogCritical(ex, "Database migration/seeding failed at startup.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("frontend");

app.MapControllers();

app.Run();