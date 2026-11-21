namespace TaskTracker2.Models;

public sealed class TaskItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public bool IsComplete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? ListId { get; set; }
    public List<SubtaskItem> Subtasks { get; set; } = [];

    public TaskItem Clone() => new()
    {
        Id = Id,
        Title = Title,
        Notes = Notes,
        DueDate = DueDate,
        Priority = Priority,
        IsComplete = IsComplete,
        CreatedAt = CreatedAt,
        ListId = ListId,
        Subtasks = Subtasks.Select(s => s.Clone()).ToList()
    };
}
