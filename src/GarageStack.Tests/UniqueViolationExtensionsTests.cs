using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageStack.Tests;

public class UniqueViolationExtensionsTests
{
    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static DbUpdateException Failure(string sqlState) =>
        new("save failed", new PostgresException("duplicate key", "ERROR", "ERROR", sqlState));

    [Fact]
    public async Task Retry_RunsTheAttemptAgainAfterAUniqueViolation()
    {
        await using var db = NewDb();
        var attempts = 0;
        var retries = 0;

        await db.RetryOnceOnUniqueViolationAsync(() =>
        {
            attempts++;
            return attempts == 1 ? throw Failure(PostgresErrorCodes.UniqueViolation) : Task.CompletedTask;
        }, () => retries++);

        Assert.Equal(2, attempts);
        Assert.Equal(1, retries);
    }

    [Fact]
    public async Task Retry_LetsAnyOtherFailureThrough()
    {
        await using var db = NewDb();
        var attempts = 0;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.RetryOnceOnUniqueViolationAsync(() =>
        {
            attempts++;
            throw Failure(PostgresErrorCodes.ForeignKeyViolation);
        }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Retry_GivesUpAfterASecondViolation()
    {
        await using var db = NewDb();
        var attempts = 0;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.RetryOnceOnUniqueViolationAsync(() =>
        {
            attempts++;
            throw Failure(PostgresErrorCodes.UniqueViolation);
        }));

        Assert.Equal(2, attempts);
    }
}
