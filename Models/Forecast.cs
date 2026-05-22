using System.ComponentModel.DataAnnotations.Schema;

namespace MyWeatherApp.Models;

public class Forecast
{
    public int ForecastId { get; set; }

    // Foreign key + navigation property til Application.
    // AppId matcher ikke EF Cores FK-konvention (forventer ApplicationId/ApplicationAppId),
    // så vi binder den eksplicit med [ForeignKey].
    [ForeignKey(nameof(Application))]
    public int AppId { get; set; }
    public Application Application { get; set; } = null!;

    // Foreign key + navigation property til Location
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;

    // Hvornår vi hentede prognosen fra API'et (UTC)
    [Column(TypeName = "timestamp with time zone")]
    public DateTime RetrievedAt { get; set; }

    // Hvilket tidspunkt prognosen er FOR (UTC)
    [Column(TypeName = "timestamp with time zone")]
    public DateTime TargetDateTime { get; set; }

    public double PredTemp { get; set; }
}
