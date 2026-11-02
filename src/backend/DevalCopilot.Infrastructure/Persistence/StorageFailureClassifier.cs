using Microsoft.Data.Sqlite;

namespace DevalCopilot.Infrastructure.Persistence;

/// <summary>
/// ADR-0072: decides, from an exception's type and SQLite primary result code only (never its message), whether a startup failure is
/// ordinary storage unavailability the person can restore: the database file or directory could not be opened, read or written, is
/// read-only, full, locked or busy, or an input/output error occurred. Every other database error (a schema, query, constraint,
/// corruption or not-a-database error) is not storage unavailability and must be treated as incoherent state.
/// </summary>
public static class StorageFailureClassifier
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteReadOnly = 8;
    private const int SqliteInputOutput = 10;
    private const int SqliteFull = 13;
    private const int SqliteCannotOpen = 14;
    private const int SqlitePermission = 3;

    public static bool IsStorageUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case SqliteException sqlite:
                    return (sqlite.SqliteErrorCode & 0xFF) is SqliteBusy or SqliteLocked or SqliteReadOnly or SqliteInputOutput or SqliteFull
                        or SqliteCannotOpen or SqlitePermission;
                case IOException or UnauthorizedAccessException:
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether the exception chain contains a database provider error at all.</summary>
    public static bool IsDatabaseError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return true;
            }
        }

        return false;
    }
}
