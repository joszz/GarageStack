using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<TelemetrySnapshot> TelemetrySnapshots => Set<TelemetrySnapshot>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();

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
            e.HasIndex(s => new { s.VehicleId, s.RawTopic })
             .HasFilter("\"RawTopic\" IS NOT NULL");
            e.HasIndex(s => new { s.VehicleId, s.Latitude, s.Longitude, s.RecordedAt })
             .HasDatabaseName("IX_TelemetrySnapshots_VehicleId_LatLon_RecordedAt")
             .HasFilter("\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL");
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

    }
}
