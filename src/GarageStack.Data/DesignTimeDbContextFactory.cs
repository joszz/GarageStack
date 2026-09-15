using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GarageStack.Data;

/// <summary>
/// Lets the EF Core tooling (dotnet ef migrations add, has-pending-model-changes, ...) build the
/// model without starting the Api host, which refuses to start unless a sign-in method and a
/// connection string are configured. Adding a migration never opens a connection, so the
/// placeholder connection string below is only there to select the Npgsql provider.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=garagestack-design;Username=design;Password=design")
            .Options;
        return new AppDbContext(options);
    }
}
