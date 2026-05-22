namespace MyWeatherApp.Models;

public class Location
{
    public int LocationId { get; set; }
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // ID på den station hos Meteostat hvor vi henter observationer for denne lokation.
    // F.eks. "06180" for København Lufthavn.
    public string ObservationStationId { get; set; } = "";

    // Navigation properties
    public ICollection<Forecast> Forecasts { get; set; } = new List<Forecast>();
    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
}