using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GarageStack.Tests;

// Every migration, from the first to the newest, applied to an empty PostgreSQL database: what a
// new install does on its first start, and what an upgrade does in parts.
public class MigrationsPostgresTests
{
    [Fact]
    public async Task EveryMigration_AppliesToAnEmptyDatabase_AndLeavesNoneToRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();

        var applied = await db.Database.GetAppliedMigrationsAsync(ct);
        var pending = await db.Database.GetPendingMigrationsAsync(ct);

        Assert.Equal(db.Database.GetMigrations(), applied);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task TheNewestMigrations_CanBeTakenBackAndAppliedAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var migrations = db.Database.GetMigrations().ToList();
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();

        // Back to before the last three, which rolling back a release would do, and forwards again.
        await migrator.MigrateAsync(migrations[^4], cancellationToken: ct);
        await migrator.MigrateAsync(cancellationToken: ct);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
    }
}
