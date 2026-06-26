namespace GarageStack.Core.Models;

public class PoiCacheTile
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string PoiType { get; set; } = string.Empty;
    public int CellLat { get; set; }
    public int CellLng { get; set; }
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
