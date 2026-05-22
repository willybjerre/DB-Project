using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyWeatherApp.Models;

public class Observation
{
    [Key]
    public int ObsId { get; set; }

    // Foreign key + navigation property til Location
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;

    // Det faktiske tidspunkt observationen gælder for (UTC)
    [Column(TypeName = "timestamp with time zone")]
    public DateTime ObsAt { get; set; }
    public double Temp { get; set; }
}
