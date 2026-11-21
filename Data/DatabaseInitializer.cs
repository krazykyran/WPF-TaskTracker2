using Microsoft.Data.Sqlite;

namespace TaskTracker2.Data;

public sealed class DatabaseInitializer
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public DatabaseInitializer(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = DatabasePaths.BuildConnectionString(databasePath);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        DatabasePaths.EnsureDirectoryExists(_databasePath);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // Ensure base Tasks table matches the existing schema if creating a brand-new DB
            await ExecuteNonQueryAsync(connection, """
                CREATE TABLE IF NOT EXISTS Tasks (
                    Id INTEGER PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Notes TEXT,
                    DueDate TEXT,
                    Priority INTEGER NOT NULL,
                    IsComplete INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                """, cancellationToken, transaction);

            // Additive table: Custom Lists / Categories
            await ExecuteNonQueryAsync(connection, """
                CREATE TABLE IF NOT EXISTS TaskLists (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    ColorHex TEXT NOT NULL DEFAULT '#3B82F6',
                    IconName TEXT NOT NULL DEFAULT 'List24',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );
                """, cancellationToken, transaction);

            // Additive nullable column on Tasks (100% backward-compatible with existing rows)
            if (!await ColumnExistsAsync(connection, "Tasks", "ListId", cancellationToken, transaction))
            {
                await ExecuteNonQueryAsync(connection, """
                    ALTER TABLE Tasks
                    ADD COLUMN ListId INTEGER REFERENCES TaskLists(Id) ON DELETE SET NULL;
                    """, cancellationToken, transaction);
            }

            // Additive table: Subtasks / Checklist items
            await ExecuteNonQueryAsync(connection, """
                CREATE TABLE IF NOT EXISTS Subtasks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TaskId INTEGER NOT NULL REFERENCES Tasks(Id) ON DELETE CASCADE,
                    Title TEXT NOT NULL,
                    IsComplete INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );
                """, cancellationToken, transaction);

            // Additive table: Key-value app preferences (theme, sort order)
            await ExecuteNonQueryAsync(connection, """
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );
                """, cancellationToken, transaction);

            await ExecuteNonQueryAsync(connection, """
                CREATE INDEX IF NOT EXISTS IX_Tasks_ListId ON Tasks(ListId);
                CREATE INDEX IF NOT EXISTS IX_Subtasks_TaskId ON Subtasks(TaskId);
                """, cancellationToken, transaction);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken,
        SqliteTransaction transaction)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var existingName = reader.GetString(1);
            if (string.Equals(existingName, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
