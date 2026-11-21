namespace TaskTracker2.Models;

public sealed class SubtaskItem
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public int SortOrder { get; set; }

    public SubtaskItem Clone() => new()
    {
        Id = Id,
        TaskId = TaskId,
        Title = Title,
        IsComplete = IsComplete,
        SortOrder = SortOrder
    };
}
