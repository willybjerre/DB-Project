using Microsoft.EntityFrameworkCore;
using MyWeatherApp.Models;

namespace MyWeatherApp.Data;

public class WeatherDbContext : DbContext
{
    public WeatherDbContext(DbContextOptions<WeatherDbContext> options) : base(options)
    {
    }

    // Hver DbSet bliver til en tabel i databasen
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Forecast> Forecasts => Set<Forecast>();
    public DbSet<Observation> Observations => Set<Observation>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Forhindr duplikerede prognoser:
        // Vi vil ikke have to rækker for "Yr's prognose for København for i morgen kl. 10"
        mb.Entity<Forecast>()
            .HasIndex(f => new { f.AppId, f.LocationId, f.TargetDateTime })
            .IsUnique();

        // Forhindr duplikerede observationer:
        // Hver lokation kan kun have én observation per tidspunkt
        mb.Entity<Observation>()
            .HasIndex(o => new { o.LocationId, o.ObsAt })
            .IsUnique();
    }
}