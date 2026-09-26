using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageStack.Data.Extensions;

/// <summary>
/// Two writers (the Api and the Worker, or two browsers asking about the same thing) can insert
/// the same unique row at once, and the second save fails with PostgreSQL's unique-violation
/// error. By then the first writer's row is committed, so doing the same work again finds it and
/// updates it instead.
/// </summary>
public static class UniqueViolationExtensions
{
    /// <summary>Whether the save failed because a unique index already held the key.</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>
    /// Runs <paramref name="attempt"/>, and once more when another writer inserted the same unique
    /// row first. The failed insert is dropped from the change tracker before the retry, so the
    /// second attempt starts from what the database now holds. <paramref name="onRetry"/>, when
    /// given, runs just before the retry, for a caller that wants it logged.
    /// </summary>
    public static async Task RetryOnceOnUniqueViolationAsync(this DbContext db, Func<Task> attempt, Action? onRetry = null)
    {
        try
        {
            await attempt();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            onRetry?.Invoke();
            db.ChangeTracker.Clear();
            await attempt();
        }
    }
}
