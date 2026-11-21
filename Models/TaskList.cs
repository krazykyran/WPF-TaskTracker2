namespace TaskTracker2.Models;

public sealed class TaskList
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#3B82F6";
    public string IconName { get; set; } = "List24";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TaskList Clone() => new()
    {
        Id = Id,
        Name = Name,
        ColorHex = ColorHex,
        IconName = IconName,
        SortOrder = SortOrder,
        CreatedAt = CreatedAt
    };
}
