using Npgsql;

namespace GarageStack.Data.Repositories;

internal static class PostgresErrors
{
    /// <summary>
    /// PostgreSQL refused a transaction because another one changed the same rows (40001). EF hands
    /// it over wrapped, in a DbUpdateException and then in Npgsql's "likely due to a transient
    /// failure" InvalidOperationException, so the whole chain is searched.
    /// </summary>
    public static bool IsSerializationFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure }) return true;
        }
        return false;
    }
}
