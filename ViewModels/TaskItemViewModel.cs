using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskTracker2.Models;

namespace TaskTracker2.ViewModels;

public partial class TaskItemViewModel : ObservableObject
{
    public TaskItem Model { get; }

    public long Id => Model.Id;
    public DateTime CreatedAt => Model.CreatedAt;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string? _notes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDueDate))]
    [NotifyPropertyChangedFor(nameof(DueDateDisplay))]
    [NotifyPropertyChangedFor(nameof(IsOverdue))]
    [NotifyPropertyChangedFor(nameof(IsDueToday))]
    private DateOnly? _dueDate;

    [ObservableProperty]
    private TaskPriority _priority;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverdue))]
    private bool _isComplete;

    [ObservableProperty]
    private long? _listId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasList))]
    private string? _listName;

    [ObservableProperty]
    private string _listColorHex = "#3B82F6";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtasks))]
    [NotifyPropertyChangedFor(nameof(SubtaskSummary))]
    private int _subtaskTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtaskSummary))]
    private int _subtaskCompleted;

    [ObservableProperty]
    private bool _isSelected;

    public TaskItemViewModel(TaskItem model)
    {
        Model = model;
        _title = model.Title;
        _notes = model.Notes;
        _dueDate = model.DueDate;
        _priority = model.Priority;
        _isComplete = model.IsComplete;
        _listId = model.ListId;
        RefreshSubtaskStats();
    }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasDueDate => DueDate.HasValue;
    public bool HasList => !string.IsNullOrWhiteSpace(ListName);
    public bool HasSubtasks => SubtaskTotal > 0;
    public string SubtaskSummary => SubtaskTotal > 0 ? $"{SubtaskCompleted}/{SubtaskTotal}" : string.Empty;

    public bool IsOverdue =>
        !IsComplete &&
        DueDate.HasValue &&
        DueDate.Value < DateOnly.FromDateTime(DateTime.Today);

    public bool IsDueToday =>
        DueDate.HasValue &&
        DueDate.Value == DateOnly.FromDateTime(DateTime.Today);

    public string DueDateDisplay
    {
        get
        {
            if (!DueDate.HasValue)
            {
                return string.Empty;
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var d = DueDate.Value;

            if (d == today) return "Today";
            if (d == today.AddDays(1)) return "Tomorrow";
            if (d == today.AddDays(-1)) return "Yesterday";

            var formatted = d.Year == today.Year
                ? d.ToString("MMM d", CultureInfo.CurrentCulture)
                : d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

            return formatted;
        }
    }

    public string CreatedAtDisplay =>
        $"Created {CreatedAt.ToLocalTime():MMM d, yyyy 'at' h:mm tt}";

    public void RefreshSubtaskStats()
    {
        SubtaskTotal = Model.Subtasks.Count;
        SubtaskCompleted = Model.Subtasks.Count(s => s.IsComplete);
    }

    public void UpdateListMetadata(TaskList? list)
    {
        ListName = list?.Name;
        ListColorHex = list?.ColorHex ?? "#3B82F6";
    }

    public void SyncModelFromProperties()
    {
        Model.Title = Title;
        Model.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;
        Model.DueDate = DueDate;
        Model.Priority = Priority;
        Model.IsComplete = IsComplete;
        Model.ListId = ListId;
    }

    public void SyncPropertiesFromModel()
    {
        Title = Model.Title;
        Notes = Model.Notes;
        DueDate = Model.DueDate;
        Priority = Model.Priority;
        IsComplete = Model.IsComplete;
        ListId = Model.ListId;
        RefreshSubtaskStats();
    }
}
