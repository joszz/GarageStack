namespace GarageStack.Core.Models;

public class PoiItem
{
    public long Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string PoiType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Name { get; set; }
    public string? MetaJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int CellLat { get; set; }
    public int CellLng { get; set; }
}
