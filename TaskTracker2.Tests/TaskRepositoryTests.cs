using System.IO;
using Microsoft.Data.Sqlite;
using TaskTracker2.Data;
using TaskTracker2.Models;
using Xunit;

namespace TaskTracker2.Tests;

public sealed class TaskRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public TaskRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TaskTracker2Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore transient file lock on cleanup
            }
        }
    }

    private string CreateTempDbPath(string name = "tasks.db") => Path.Combine(_tempDir, name);

    [Fact]
    public async Task InitializeAsync_MigratesLegacyTasksDb_PreservesAllExistingRows()
    {
        var tempDb = CreateTempDbPath("legacy_copy.db");
        await SeedLegacySchemaAsync(tempDb);

        var initializer = new DatabaseInitializer(tempDb);
        await initializer.InitializeAsync();
        // Run a second time to verify idempotency
        await initializer.InitializeAsync();

        var repo = new TaskRepository(tempDb);
        var tasks = await repo.GetAllTasksAsync();

        Assert.True(tasks.Count >= 4);

        var dinner = tasks.FirstOrDefault(t => t.Title == "Cook dinner for Saturday night");
        Assert.NotNull(dinner);
        Assert.True(dinner.IsComplete);
        Assert.Equal(TaskPriority.Low, dinner.Priority);
        Assert.Equal(new DateOnly(2026, 11, 21), dinner.DueDate);
        Assert.Null(dinner.ListId);

        var dishes = tasks.FirstOrDefault(t => t.Title == "Wash the dishes");
        Assert.NotNull(dishes);
        Assert.False(dishes.IsComplete);
        Assert.Equal(TaskPriority.Low, dishes.Priority);
        Assert.Null(dishes.DueDate);

        var backLawn = tasks.FirstOrDefault(t => t.Title == "Mow the back lawn");
        Assert.NotNull(backLawn);
        Assert.Equal(new DateOnly(2026, 11, 22), backLawn.DueDate);
    }

    [Fact]
    public async Task TaskCrud_PriorityScale_StoresExactIntegers1To3AndIsoDueDate()
    {
        var tempDb = CreateTempDbPath("crud.db");
        await new DatabaseInitializer(tempDb).InitializeAsync();
        var repo = new TaskRepository(tempDb);

        var low = await repo.CreateTaskAsync(new TaskItem
        {
            Title = "Low task",
            Priority = TaskPriority.Low,
            DueDate = new DateOnly(2026, 12, 1)
        });
        var normal = await repo.CreateTaskAsync(new TaskItem
        {
            Title = "Normal task",
            Priority = TaskPriority.Normal
        });
        var high = await repo.CreateTaskAsync(new TaskItem
        {
            Title = "High task",
            Priority = TaskPriority.High,
            Notes = "Urgent detail"
        });

        // Verify raw SQLite integers match 1=Low, 2=Normal, 3=High
        await using (var conn = new SqliteConnection(DatabasePaths.BuildConnectionString(tempDb)))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Priority, DueDate FROM Tasks WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", low.Id);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal("2026-12-01", reader.GetString(1));
        }

        normal.Title = "Updated normal";
        normal.Priority = TaskPriority.High;
        normal.IsComplete = true;
        await repo.UpdateTaskAsync(normal);

        var all = await repo.GetAllTasksAsync();
        Assert.Equal(3, all.Count);
        var loadedNormal = all.Single(t => t.Id == normal.Id);
        Assert.Equal("Updated normal", loadedNormal.Title);
        Assert.Equal(TaskPriority.High, loadedNormal.Priority);
        Assert.True(loadedNormal.IsComplete);
        Assert.Equal("Urgent detail", all.Single(t => t.Id == high.Id).Notes);
    }

    [Fact]
    public async Task CustomLists_DeleteList_KeepsTasksAndClearsListId()
    {
        var tempDb = CreateTempDbPath("lists.db");
        await new DatabaseInitializer(tempDb).InitializeAsync();
        var repo = new TaskRepository(tempDb);

        var workList = await repo.CreateListAsync(new TaskList
        {
            Name = "Work",
            ColorHex = "#8B5CF6",
            IconName = "Briefcase24"
        });

        var task = await repo.CreateTaskAsync(new TaskItem
        {
            Title = "Prepare slides",
            Priority = TaskPriority.Normal,
            ListId = workList.Id
        });

        await repo.DeleteListAsync(workList.Id);

        var lists = await repo.GetAllListsAsync();
        Assert.Empty(lists);

        var tasks = await repo.GetAllTasksAsync();
        var loaded = Assert.Single(tasks);
        Assert.Equal(task.Id, loaded.Id);
        Assert.Null(loaded.ListId);
    }

    [Fact]
    public async Task Subtasks_AndUndoRestore_RestoresFullTaskAndSubtasks()
    {
        var tempDb = CreateTempDbPath("undo.db");
        await new DatabaseInitializer(tempDb).InitializeAsync();
        var repo = new TaskRepository(tempDb);

        var task = await repo.CreateTaskAsync(new TaskItem
        {
            Title = "Plan weekend trip",
            Notes = "Pack hiking boots",
            DueDate = new DateOnly(2026, 11, 28),
            Priority = TaskPriority.High
        });

        var sub1 = await repo.CreateSubtaskAsync(new SubtaskItem
        {
            TaskId = task.Id,
            Title = "Book cabin",
            IsComplete = true,
            SortOrder = 0
        });
        var sub2 = await repo.CreateSubtaskAsync(new SubtaskItem
        {
            TaskId = task.Id,
            Title = "Buy snacks",
            IsComplete = false,
            SortOrder = 1
        });
        task.Subtasks.Add(sub1);
        task.Subtasks.Add(sub2);

        var snapshot = task.Clone();

        await repo.DeleteTaskAsync(task.Id);
        Assert.Empty(await repo.GetAllTasksAsync());

        // Restore via Undo
        await repo.RestoreTaskAsync(snapshot);

        var restored = Assert.Single(await repo.GetAllTasksAsync());
        Assert.Equal(task.Id, restored.Id);
        Assert.Equal("Plan weekend trip", restored.Title);
        Assert.Equal("Pack hiking boots", restored.Notes);
        Assert.Equal(TaskPriority.High, restored.Priority);
        Assert.Equal(2, restored.Subtasks.Count);
        Assert.True(restored.Subtasks[0].IsComplete);
        Assert.Equal("Book cabin", restored.Subtasks[0].Title);
        Assert.False(restored.Subtasks[1].IsComplete);
        Assert.Equal("Buy snacks", restored.Subtasks[1].Title);
    }

    [Fact]
    public async Task Settings_UpsertAndReadBack()
    {
        var tempDb = CreateTempDbPath("settings.db");
        await new DatabaseInitializer(tempDb).InitializeAsync();
        var repo = new TaskRepository(tempDb);

        Assert.Null(await repo.GetSettingAsync("Theme"));
        await repo.SetSettingAsync("Theme", "Dark");
        Assert.Equal("Dark", await repo.GetSettingAsync("Theme"));

        await repo.SetSettingAsync("Theme", "Light");
        Assert.Equal("Light", await repo.GetSettingAsync("Theme"));
    }

    private static async Task SeedLegacySchemaAsync(string dbPath)
    {
        await using var conn = new SqliteConnection(DatabasePaths.BuildConnectionString(dbPath));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Tasks (
                Id INTEGER PRIMARY KEY,
                Title TEXT NOT NULL,
                Notes TEXT,
                DueDate TEXT,
                Priority INTEGER NOT NULL,
                IsComplete INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            INSERT INTO Tasks VALUES (1, 'Cook dinner for Saturday night', NULL, '2026-11-21', 1, 1, '2026-11-21T08:37:48.1902122Z');
            INSERT INTO Tasks VALUES (2, 'Wash the dishes', NULL, NULL, 1, 0, '2026-11-21T08:38:59.2849633Z');
            INSERT INTO Tasks VALUES (3, 'Mow the front lawn', '', '2026-11-21', 1, 0, '2026-11-21T09:49:57.1036543Z');
            INSERT INTO Tasks VALUES (4, 'Mow the back lawn', '', '2026-11-22', 1, 0, '2026-11-21T09:53:31.1030053Z');
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
