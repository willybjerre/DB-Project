using System.ComponentModel.DataAnnotations;

namespace MyWeatherApp.Models;

public class Application
{
    [Key]
    public int AppId { get; set; }
    public string Name { get; set; } = "";

    // Navigation property: alle forecasts lavet af denne app
    public ICollection<Forecast> Forecasts { get; set; } = new List<Forecast>();
}
