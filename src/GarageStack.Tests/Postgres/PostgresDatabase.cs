using GarageStack.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageStack.Tests;

/// <summary>
/// A database of its own on a real PostgreSQL server, for the tests the in-memory provider cannot
/// answer: unique and filtered indexes, concurrency tokens, bulk updates and deletes, the SQL a
/// LINQ query becomes, and NOTIFY. Each one is a copy of a template the migrations built once per
/// test run, so a test starts from the schema a deployment has and costs a CREATE DATABASE.
/// <para>
/// The server is <see cref="ConnectionVariable"/>, a connection string for a role that may create
/// databases, such as <c>Host=localhost;Username=postgres;Password=postgres</c>. Without it a test
/// is skipped, except in CI (<c>CI=true</c>), where the job provides one and a missing one fails:
/// these tests must never pass there by not running.
/// </para>
/// </summary>
internal sealed class PostgresDatabase : IAsyncDisposable
{
    public const string ConnectionVariable = "GARAGESTACK_TEST_POSTGRES";

    private const string TemplateName = "garagestack_test_template";
    private static readonly SemaphoreSlim TemplateGate = new(1, 1);
    private static bool _templateReady;

    private readonly string _adminConnection;
    private readonly string _name;

    private PostgresDatabase(string adminConnection, string name)
    {
        _adminConnection = adminConnection;
        _name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = name }.ConnectionString;
    }

    public string ConnectionString { get; }

    public static async Task<PostgresDatabase> CreateAsync(CancellationToken ct)
    {
        var admin = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(admin))
        {
            const string howTo = $"set {ConnectionVariable} to a PostgreSQL server, for example "
                + "docker run -d -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:18-alpine and "
                + $"{ConnectionVariable}=\"Host=localhost;Username=postgres;Password=postgres\"";
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                Assert.Fail($"The PostgreSQL tests need a server in CI: {howTo}");
            Assert.Skip($"Needs PostgreSQL: {howTo}");
        }

        var name = $"garagestack_test_{Guid.NewGuid():N}"[..40];
        // One copy at a time: PostgreSQL refuses to copy a template another session is reading.
        await TemplateGate.WaitAsync(ct);
        try
        {
            await EnsureTemplateAsync(admin, ct);
            await ExecuteAsync(admin, $"""CREATE DATABASE "{name}" TEMPLATE "{TemplateName}" """, ct);
        }
        finally
        {
            TemplateGate.Release();
        }
        return new PostgresDatabase(admin, name);
    }

    /// <summary>A context on this database, as the services get one. Dispose it.</summary>
    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        // A pooled connection still open on the database would keep it from being dropped.
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(_adminConnection, $"""DROP DATABASE IF EXISTS "{_name}" WITH (FORCE)""", CancellationToken.None);
    }

    // Built afresh by the first test of each run, so a migration added since the last run is in it.
    // Called holding TemplateGate.
    private static async Task EnsureTemplateAsync(string admin, CancellationToken ct)
    {
        if (_templateReady) return;

        await ExecuteAsync(admin, $"""DROP DATABASE IF EXISTS "{TemplateName}" WITH (FORCE)""", ct);
        await ExecuteAsync(admin, $"""CREATE DATABASE "{TemplateName}" """, ct);
        var template = new NpgsqlConnectionStringBuilder(admin) { Database = TemplateName }.ConnectionString;
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(template).Options))
            await db.Database.MigrateAsync(ct);

        // A template cannot be copied while anything is connected to it.
        NpgsqlConnection.ClearAllPools();
        _templateReady = true;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
