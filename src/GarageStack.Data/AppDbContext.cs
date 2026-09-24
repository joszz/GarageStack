using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<TelemetrySnapshot> TelemetrySnapshots => Set<TelemetrySnapshot>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();
    public DbSet<PoiItem> PoiItems => Set<PoiItem>();
    public DbSet<PoiCacheTile> PoiCacheTiles => Set<PoiCacheTile>();
    public DbSet<GeocodeCacheEntry> GeocodeCacheEntries => Set<GeocodeCacheEntry>();
    public DbSet<MapMatchCacheEntry> MapMatchCacheEntries => Set<MapMatchCacheEntry>();
    public DbSet<MaintenanceItem> MaintenanceItems => Set<MaintenanceItem>();
    public DbSet<MaintenanceLogEntry> MaintenanceLogEntries => Set<MaintenanceLogEntry>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>(e =>
        {
            e.HasKey(v => v.Id);
            e.HasIndex(v => v.Vin).IsUnique();
            e.Property(v => v.Vin).HasMaxLength(17).IsRequired();
        });

        modelBuilder.Entity<TelemetrySnapshot>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.VehicleId, s.RecordedAt });
            // IX_TelemetrySnapshots_VehicleId_RecordedAt_Chart is a partial index
            // managed via raw SQL in the AddChartIndex migration.
            e.HasIndex(s => new { s.VehicleId, s.RawTopic })
             .HasFilter("\"RawTopic\" IS NOT NULL");
            e.HasIndex(s => new { s.VehicleId, s.Latitude, s.Longitude, s.RecordedAt })
             .HasDatabaseName("IX_TelemetrySnapshots_VehicleId_LatLon_RecordedAt")
             .HasFilter("\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL");
            // Backs GetMergedLatestAsync's schedule fallback, which looks for the newest row
            // carrying any charging or heating schedule. Schedules are only published when the
            // user changes one, so most vehicles have no such row at all: without this filtered
            // index that lookup reads the entire history on every status cache miss, which is
            // after every telemetry write. The index only holds the rare rows that match.
            e.HasIndex(
                s => new { s.VehicleId, s.RecordedAt },
                "IX_TelemetrySnapshots_VehicleId_RecordedAt_Schedule")
             .HasFilter(
                 "\"ChargingScheduleMode\" IS NOT NULL OR \"ChargingScheduleStartTime\" IS NOT NULL " +
                 "OR \"ChargingScheduleEndTime\" IS NOT NULL OR \"BatteryHeatingScheduleMode\" IS NOT NULL " +
                 "OR \"BatteryHeatingScheduleStartTime\" IS NOT NULL");
            e.HasOne(s => s.Vehicle)
             .WithMany(v => v.TelemetrySnapshots)
             .HasForeignKey(s => s.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PushSubscription>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Endpoint).IsUnique();
        });

        modelBuilder.Entity<AppNotification>(e =>
        {
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.CreatedAt);
            e.HasIndex(n => new { n.IsDeleted, n.CreatedAt });
        });

        modelBuilder.Entity<PoiItem>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.Source, p.ExternalId })
             .IsUnique()
             .HasDatabaseName("IX_PoiItems_Source_ExternalId");
            e.HasIndex(p => new { p.Source, p.PoiType, p.CellLat, p.CellLng })
             .HasDatabaseName("IX_PoiItems_Source_PoiType_CellLatLng");
            // Backs GetPoisInBoundsAsync's lat/lng range query - without this, that query can
            // only narrow to the Source+PoiType partition via the index above and then scans
            // every row in it, since neither Latitude nor Longitude appear in any other index.
            e.HasIndex(p => new { p.Source, p.PoiType, p.Latitude, p.Longitude })
             .HasDatabaseName("IX_PoiItems_Source_PoiType_LatLon");
            // Backs GetDistinctBrandsAsync: the brand filter list is a DISTINCT over this index
            // instead of a scan that deserializes every row's MetaJson.
            e.HasIndex(p => new { p.Source, p.PoiType, p.Brand })
             .HasDatabaseName("IX_PoiItems_Source_PoiType_Brand");
            e.Property(p => p.Source).HasMaxLength(32).IsRequired();
            e.Property(p => p.PoiType).HasMaxLength(32).IsRequired();
            e.Property(p => p.ExternalId).HasMaxLength(64).IsRequired();
            e.Property(p => p.Brand).HasMaxLength(PoiItemLimits.BrandMaxLength);
        });

        modelBuilder.Entity<PoiCacheTile>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => new { t.Source, t.PoiType, t.CellLat, t.CellLng })
             .IsUnique()
             .HasDatabaseName("IX_PoiCacheTiles_Source_PoiType_CellLatLng");
            e.HasIndex(t => t.ExpiresAt)
             .HasDatabaseName("IX_PoiCacheTiles_ExpiresAt");
            e.Property(t => t.Source).HasMaxLength(32).IsRequired();
            e.Property(t => t.PoiType).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<GeocodeCacheEntry>(e =>
        {
            e.HasKey(g => g.Id);
            // One row per cell per language, which is also the lookup GetValidAsync runs.
            e.HasIndex(g => new { g.Precision, g.Language, g.CellLat, g.CellLng })
             .IsUnique()
             .HasDatabaseName("IX_GeocodeCacheEntries_Precision_Language_CellLatLng");
            e.HasIndex(g => g.ExpiresAt)
             .HasDatabaseName("IX_GeocodeCacheEntries_ExpiresAt");
            e.Property(g => g.Precision).HasMaxLength(GeocodeCacheLimits.PrecisionMaxLength).IsRequired();
            e.Property(g => g.Language).HasMaxLength(GeocodeCacheLimits.LanguageMaxLength).IsRequired();
            e.Property(g => g.DisplayName).HasMaxLength(GeocodeCacheLimits.DisplayNameMaxLength);
            e.Property(g => g.Road).HasMaxLength(GeocodeCacheLimits.NameMaxLength);
            e.Property(g => g.HouseNumber).HasMaxLength(GeocodeCacheLimits.NameMaxLength);
            e.Property(g => g.City).HasMaxLength(GeocodeCacheLimits.NameMaxLength);
            e.Property(g => g.Postcode).HasMaxLength(GeocodeCacheLimits.PostcodeMaxLength);
            e.Property(g => g.CountryCode).HasMaxLength(GeocodeCacheLimits.CountryCodeMaxLength);
        });

        modelBuilder.Entity<MapMatchCacheEntry>(e =>
        {
            e.HasKey(m => m.Id);
            // One row per trace per matcher, which is also the lookup GetValidAsync runs.
            e.HasIndex(m => new { m.Provider, m.TraceHash })
             .IsUnique()
             .HasDatabaseName("IX_MapMatchCacheEntries_Provider_TraceHash");
            e.HasIndex(m => m.ExpiresAt)
             .HasDatabaseName("IX_MapMatchCacheEntries_ExpiresAt");
            e.Property(m => m.Provider).HasMaxLength(MapMatchCacheLimits.ProviderMaxLength).IsRequired();
            e.Property(m => m.TraceHash).HasMaxLength(MapMatchCacheLimits.TraceHashLength).IsRequired();
        });

        modelBuilder.Entity<MaintenanceItem>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.VehicleId);
            e.Property(m => m.Name).HasMaxLength(200).IsRequired();
            e.Property(m => m.Notes).HasMaxLength(1000);
            e.HasOne(m => m.Vehicle)
             .WithMany()
             .HasForeignKey(m => m.VehicleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MaintenanceLogEntry>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasIndex(l => new { l.MaintenanceItemId, l.PerformedAt });
            e.Property(l => l.Notes).HasMaxLength(1000);
            e.HasOne(l => l.MaintenanceItem)
             .WithMany(m => m.LogEntries)
             .HasForeignKey(l => l.MaintenanceItemId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RevokedToken>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.Jti).IsUnique();
            e.Property(r => r.Jti).HasMaxLength(64).IsRequired();
        });
    }
}

/// <summary>Column limits shared between the model configuration and the ingest code that must respect them.</summary>
public static class PoiItemLimits
{
    public const int BrandMaxLength = 128;
}
