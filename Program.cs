using Microsoft.EntityFrameworkCore;
using MyWeatherApp.Data;
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

builder.Services.AddScoped<ForecastIngestionService>();

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();