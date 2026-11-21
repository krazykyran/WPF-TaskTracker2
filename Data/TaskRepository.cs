using System.Globalization;
using Microsoft.Data.Sqlite;
using TaskTracker2.Models;

namespace TaskTracker2.Data;

public sealed class TaskRepository
{
    private readonly string _connectionString;

    public TaskRepository(string databasePath)
    {
        _connectionString = DatabasePaths.BuildConnectionString(databasePath);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    // --- Tasks ---

    public async Task<List<TaskItem>> GetAllTasksAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var tasks = new List<TaskItem>();
        var taskById = new Dictionary<long, TaskItem>();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id, Title, Notes, DueDate, Priority, IsComplete, CreatedAt, ListId
                FROM Tasks
                ORDER BY Id ASC;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var task = new TaskItem
                {
                    Id = reader.GetInt64(0),
                    Title = reader.GetString(1),
                    Notes = reader.IsDBNull(2) ? null : reader.GetString(2),
                    DueDate = reader.IsDBNull(3) ? null : ParseDateOnly(reader.GetString(3)),
                    Priority = ParsePriority(reader.GetInt32(4)),
                    IsComplete = reader.GetInt32(5) != 0,
                    CreatedAt = ParseCreatedAt(reader.GetString(6)),
                    ListId = reader.IsDBNull(7) ? null : reader.GetInt64(7)
                };
                tasks.Add(task);
                taskById[task.Id] = task;
            }
        }

        await using (var subCmd = connection.CreateCommand())
        {
            subCmd.CommandText = """
                SELECT Id, TaskId, Title, IsComplete, SortOrder
                FROM Subtasks
                ORDER BY SortOrder ASC, Id ASC;
                """;

            await using var reader = await subCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var subtask = new SubtaskItem
                {
                    Id = reader.GetInt64(0),
                    TaskId = reader.GetInt64(1),
                    Title = reader.GetString(2),
                    IsComplete = reader.GetInt32(3) != 0,
                    SortOrder = reader.GetInt32(4)
                };

                if (taskById.TryGetValue(subtask.TaskId, out var parent))
                {
                    parent.Subtasks.Add(subtask);
                }
            }
        }

        return tasks;
    }

    public async Task<TaskItem> CreateTaskAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        if (task.CreatedAt == default)
        {
            task.CreatedAt = DateTime.UtcNow;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Tasks (Title, Notes, DueDate, Priority, IsComplete, CreatedAt, ListId)
            VALUES ($title, $notes, $dueDate, $priority, $isComplete, $createdAt, $listId);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("$title", task.Title);
        cmd.Parameters.AddWithValue("$notes", (object?)task.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dueDate", task.DueDate.HasValue ? FormatDateOnly(task.DueDate.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("$priority", (int)task.Priority);
        cmd.Parameters.AddWithValue("$isComplete", task.IsComplete ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", FormatCreatedAt(task.CreatedAt));
        cmd.Parameters.AddWithValue("$listId", task.ListId.HasValue ? task.ListId.Value : DBNull.Value);

        var id = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        task.Id = id;
        return task;
    }

    public async Task UpdateTaskAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Tasks
            SET Title = $title,
                Notes = $notes,
                DueDate = $dueDate,
                Priority = $priority,
                IsComplete = $isComplete,
                ListId = $listId
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue("$id", task.Id);
        cmd.Parameters.AddWithValue("$title", task.Title);
        cmd.Parameters.AddWithValue("$notes", (object?)task.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dueDate", task.DueDate.HasValue ? FormatDateOnly(task.DueDate.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("$priority", (int)task.Priority);
        cmd.Parameters.AddWithValue("$isComplete", task.IsComplete ? 1 : 0);
        cmd.Parameters.AddWithValue("$listId", task.ListId.HasValue ? task.ListId.Value : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteTaskAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Tasks WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", taskId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TaskItem> RestoreTaskAsync(TaskItem snapshot, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Verify ListId still exists in case the list was deleted before Undo
            long? validListId = snapshot.ListId;
            if (validListId.HasValue)
            {
                await using var checkCmd = connection.CreateCommand();
                checkCmd.Transaction = transaction;
                checkCmd.CommandText = "SELECT COUNT(1) FROM TaskLists WHERE Id = $listId;";
                checkCmd.Parameters.AddWithValue("$listId", validListId.Value);
                var count = (long)(await checkCmd.ExecuteScalarAsync(cancellationToken))!;
                if (count == 0)
                {
                    validListId = null;
                }
            }

            await using (var insertCmd = connection.CreateCommand())
            {
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO Tasks (Id, Title, Notes, DueDate, Priority, IsComplete, CreatedAt, ListId)
                    VALUES ($id, $title, $notes, $dueDate, $priority, $isComplete, $createdAt, $listId);
                    """;

                insertCmd.Parameters.AddWithValue("$id", snapshot.Id);
                insertCmd.Parameters.AddWithValue("$title", snapshot.Title);
                insertCmd.Parameters.AddWithValue("$notes", (object?)snapshot.Notes ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("$dueDate", snapshot.DueDate.HasValue ? FormatDateOnly(snapshot.DueDate.Value) : DBNull.Value);
                insertCmd.Parameters.AddWithValue("$priority", (int)snapshot.Priority);
                insertCmd.Parameters.AddWithValue("$isComplete", snapshot.IsComplete ? 1 : 0);
                insertCmd.Parameters.AddWithValue("$createdAt", FormatCreatedAt(snapshot.CreatedAt));
                insertCmd.Parameters.AddWithValue("$listId", validListId.HasValue ? validListId.Value : DBNull.Value);

                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var sub in snapshot.Subtasks)
            {
                await using var subCmd = connection.CreateCommand();
                subCmd.Transaction = transaction;
                subCmd.CommandText = """
                    INSERT INTO Subtasks (Id, TaskId, Title, IsComplete, SortOrder)
                    VALUES ($id, $taskId, $title, $isComplete, $sortOrder);
                    """;
                subCmd.Parameters.AddWithValue("$id", sub.Id);
                subCmd.Parameters.AddWithValue("$taskId", snapshot.Id);
                subCmd.Parameters.AddWithValue("$title", sub.Title);
                subCmd.Parameters.AddWithValue("$isComplete", sub.IsComplete ? 1 : 0);
                subCmd.Parameters.AddWithValue("$sortOrder", sub.SortOrder);
                await subCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            var restored = snapshot.Clone();
            restored.ListId = validListId;
            return restored;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // --- Custom Lists ---

    public async Task<List<TaskList>> GetAllListsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var lists = new List<TaskList>();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, ColorHex, IconName, SortOrder, CreatedAt
            FROM TaskLists
            ORDER BY SortOrder ASC, Id ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lists.Add(new TaskList
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                ColorHex = reader.GetString(2),
                IconName = reader.GetString(3),
                SortOrder = reader.GetInt32(4),
                CreatedAt = ParseCreatedAt(reader.GetString(5))
            });
        }

        return lists;
    }

    public async Task<TaskList> CreateListAsync(TaskList list, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        if (list.CreatedAt == default)
        {
            list.CreatedAt = DateTime.UtcNow;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TaskLists (Name, ColorHex, IconName, SortOrder, CreatedAt)
            VALUES ($name, $colorHex, $iconName, $sortOrder, $createdAt);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("$name", list.Name);
        cmd.Parameters.AddWithValue("$colorHex", list.ColorHex);
        cmd.Parameters.AddWithValue("$iconName", list.IconName);
        cmd.Parameters.AddWithValue("$sortOrder", list.SortOrder);
        cmd.Parameters.AddWithValue("$createdAt", FormatCreatedAt(list.CreatedAt));

        list.Id = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        return list;
    }

    public async Task UpdateListAsync(TaskList list, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE TaskLists
            SET Name = $name,
                ColorHex = $colorHex,
                IconName = $iconName,
                SortOrder = $sortOrder
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue("$id", list.Id);
        cmd.Parameters.AddWithValue("$name", list.Name);
        cmd.Parameters.AddWithValue("$colorHex", list.ColorHex);
        cmd.Parameters.AddWithValue("$iconName", list.IconName);
        cmd.Parameters.AddWithValue("$sortOrder", list.SortOrder);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteListAsync(long listId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var unassignCmd = connection.CreateCommand())
            {
                unassignCmd.Transaction = transaction;
                unassignCmd.CommandText = "UPDATE Tasks SET ListId = NULL WHERE ListId = $id;";
                unassignCmd.Parameters.AddWithValue("$id", listId);
                await unassignCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM TaskLists WHERE Id = $id;";
                deleteCmd.Parameters.AddWithValue("$id", listId);
                await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // --- Subtasks ---

    public async Task<SubtaskItem> CreateSubtaskAsync(SubtaskItem subtask, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Subtasks (TaskId, Title, IsComplete, SortOrder)
            VALUES ($taskId, $title, $isComplete, $sortOrder);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("$taskId", subtask.TaskId);
        cmd.Parameters.AddWithValue("$title", subtask.Title);
        cmd.Parameters.AddWithValue("$isComplete", subtask.IsComplete ? 1 : 0);
        cmd.Parameters.AddWithValue("$sortOrder", subtask.SortOrder);

        subtask.Id = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        return subtask;
    }

    public async Task UpdateSubtaskAsync(SubtaskItem subtask, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Subtasks
            SET Title = $title,
                IsComplete = $isComplete,
                SortOrder = $sortOrder
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue("$id", subtask.Id);
        cmd.Parameters.AddWithValue("$title", subtask.Title);
        cmd.Parameters.AddWithValue("$isComplete", subtask.IsComplete ? 1 : 0);
        cmd.Parameters.AddWithValue("$sortOrder", subtask.SortOrder);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSubtaskAsync(long subtaskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Subtasks WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", subtaskId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // --- AppSettings ---

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // --- Parsing & Formatting Helpers ---

    public static string FormatDateOnly(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return DateOnly.FromDateTime(dt);
        }

        return null;
    }

    public static string FormatCreatedAt(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc
            ? dt.ToString("O", CultureInfo.InvariantCulture)
            : dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTime ParseCreatedAt(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime();
        }

        return DateTime.UtcNow;
    }

    public static TaskPriority ParsePriority(int value) => value switch
    {
        <= 1 => TaskPriority.Low,
        2 => TaskPriority.Normal,
        >= 3 => TaskPriority.High
    };
}
