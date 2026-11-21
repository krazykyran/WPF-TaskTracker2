using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTracker2.Data;
using TaskTracker2.Models;

namespace TaskTracker2.ViewModels;

public sealed record ListPickerOption(long? Id, string Name, string ColorHex);

public partial class TaskDetailViewModel : ObservableObject
{
    private readonly TaskRepository _repository;
    private readonly Action _onTaskMutated;
    private readonly Func<TaskItemViewModel, Task> _onDeleteRequested;
    private readonly DispatcherTimer _saveDebounceTimer;

    private bool _isLoading;
    private bool _hasPendingTextChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTask))]
    private TaskItemViewModel? _task;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private DateOnly? _dueDate;

    [ObservableProperty]
    private TaskPriority _priority = TaskPriority.Normal;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private ListPickerOption? _selectedListOption;

    [ObservableProperty]
    private string _newSubtaskTitle = string.Empty;

    [ObservableProperty]
    private string _createdAtDisplay = string.Empty;

    public ObservableCollection<SubtaskItemViewModel> Subtasks { get; } = [];
    public ObservableCollection<ListPickerOption> ListOptions { get; } = [];

    public bool HasTask => Task is not null;

    public TaskDetailViewModel(
        TaskRepository repository,
        Action onTaskMutated,
        Func<TaskItemViewModel, Task> onDeleteRequested)
    {
        _repository = repository;
        _onTaskMutated = onTaskMutated;
        _onDeleteRequested = onDeleteRequested;

        _saveDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _saveDebounceTimer.Tick += async (_, _) =>
        {
            _saveDebounceTimer.Stop();
            await FlushPendingChangesAsync();
        };
    }

    public void RefreshListOptions(IEnumerable<TaskListViewModel> lists)
    {
        var previousSelectedId = SelectedListOption?.Id ?? Task?.ListId;
        _isLoading = true;
        try
        {
            ListOptions.Clear();
            ListOptions.Add(new ListPickerOption(null, "No List", "#64748B"));
            foreach (var list in lists)
            {
                ListOptions.Add(new ListPickerOption(list.Id, list.Name, list.ColorHex));
            }

            SelectedListOption = ListOptions.FirstOrDefault(o => o.Id == previousSelectedId)
                                 ?? ListOptions[0];
        }
        finally
        {
            _isLoading = false;
        }
    }

    public async Task LoadTaskAsync(TaskItemViewModel? taskVm)
    {
        if (Task == taskVm)
        {
            return;
        }

        await FlushPendingChangesAsync();

        _isLoading = true;
        try
        {
            Task = taskVm;
            Subtasks.Clear();
            NewSubtaskTitle = string.Empty;

            if (taskVm is null)
            {
                Title = string.Empty;
                Notes = string.Empty;
                DueDate = null;
                Priority = TaskPriority.Normal;
                IsComplete = false;
                CreatedAtDisplay = string.Empty;
                SelectedListOption = ListOptions.FirstOrDefault();
                return;
            }

            Title = taskVm.Title;
            Notes = taskVm.Notes ?? string.Empty;
            DueDate = taskVm.DueDate;
            Priority = taskVm.Priority;
            IsComplete = taskVm.IsComplete;
            CreatedAtDisplay = taskVm.CreatedAtDisplay;
            SelectedListOption = ListOptions.FirstOrDefault(o => o.Id == taskVm.ListId)
                                 ?? ListOptions.FirstOrDefault();

            foreach (var sub in taskVm.Model.Subtasks)
            {
                Subtasks.Add(new SubtaskItemViewModel(sub));
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void SyncCompletionFromList(TaskItemViewModel taskVm)
    {
        if (Task != taskVm) return;
        _isLoading = true;
        try
        {
            IsComplete = taskVm.IsComplete;
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnTitleChanged(string value)
    {
        if (_isLoading || Task is null) return;
        var trimmed = value.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            Task.Title = trimmed;
            _hasPendingTextChanges = true;
            RestartDebounce();
        }
    }

    partial void OnNotesChanged(string value)
    {
        if (_isLoading || Task is null) return;
        Task.Notes = string.IsNullOrWhiteSpace(value) ? null : value;
        _hasPendingTextChanges = true;
        RestartDebounce();
    }

    partial void OnDueDateChanged(DateOnly? value)
    {
        if (_isLoading || Task is null) return;
        Task.DueDate = value;
        _ = SaveImmediatelyAsync();
    }

    partial void OnPriorityChanged(TaskPriority value)
    {
        if (_isLoading || Task is null) return;
        Task.Priority = value;
        _ = SaveImmediatelyAsync();
    }

    partial void OnIsCompleteChanged(bool value)
    {
        if (_isLoading || Task is null) return;
        Task.IsComplete = value;
        _ = SaveImmediatelyAsync();
    }

    partial void OnSelectedListOptionChanged(ListPickerOption? value)
    {
        if (_isLoading || Task is null || value is null) return;
        Task.ListId = value.Id;
        Task.ListName = value.Id.HasValue ? value.Name : null;
        Task.ListColorHex = value.ColorHex;
        _ = SaveImmediatelyAsync();
    }

    private void RestartDebounce()
    {
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    public async Task FlushPendingChangesAsync()
    {
        _saveDebounceTimer.Stop();
        if (!_hasPendingTextChanges || Task is null)
        {
            return;
        }

        _hasPendingTextChanges = false;
        await SaveImmediatelyAsync();
    }

    private async Task SaveImmediatelyAsync()
    {
        if (Task is null) return;

        Task.SyncModelFromProperties();
        await _repository.UpdateTaskAsync(Task.Model);
        _onTaskMutated();
    }

    [RelayCommand]
    private void SetDueToday() => DueDate = DateOnly.FromDateTime(DateTime.Today);

    [RelayCommand]
    private void SetDueTomorrow() => DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

    [RelayCommand]
    private void SetDueNextWeek() => DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7));

    [RelayCommand]
    private void ClearDueDate() => DueDate = null;

    [RelayCommand]
    private void SetPriority(TaskPriority priority) => Priority = priority;

    [RelayCommand]
    private async Task AddSubtaskAsync()
    {
        if (Task is null) return;
        var title = NewSubtaskTitle.Trim();
        if (string.IsNullOrEmpty(title)) return;

        var subtask = new SubtaskItem
        {
            TaskId = Task.Id,
            Title = title,
            IsComplete = false,
            SortOrder = Task.Model.Subtasks.Count
        };

        await _repository.CreateSubtaskAsync(subtask);
        Task.Model.Subtasks.Add(subtask);
        Subtasks.Add(new SubtaskItemViewModel(subtask));
        Task.RefreshSubtaskStats();
        NewSubtaskTitle = string.Empty;
        _onTaskMutated();
    }

    [RelayCommand]
    private async Task ToggleSubtaskAsync(SubtaskItemViewModel? subVm)
    {
        if (subVm is null || Task is null) return;
        subVm.Model.IsComplete = subVm.IsComplete;
        await _repository.UpdateSubtaskAsync(subVm.Model);
        Task.RefreshSubtaskStats();
        _onTaskMutated();
    }

    [RelayCommand]
    private async Task DeleteSubtaskAsync(SubtaskItemViewModel? subVm)
    {
        if (subVm is null || Task is null) return;
        await _repository.DeleteSubtaskAsync(subVm.Id);
        Task.Model.Subtasks.Remove(subVm.Model);
        Subtasks.Remove(subVm);
        Task.RefreshSubtaskStats();
        _onTaskMutated();
    }

    [RelayCommand]
    private async Task DeleteCurrentTaskAsync()
    {
        if (Task is null) return;
        _saveDebounceTimer.Stop();
        _hasPendingTextChanges = false;
        await _onDeleteRequested(Task);
    }
}
