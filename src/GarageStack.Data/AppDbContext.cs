using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<TelemetrySnapshot> TelemetrySnapshots => Set<TelemetrySnapshot>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();

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

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<UserRefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
