using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTracker2.Data;
using TaskTracker2.Models;

namespace TaskTracker2.ViewModels;

public sealed record SortOptionItem(TaskSortOption Value, string Label);

public partial class MainViewModel : ObservableObject
{
    private const string SettingThemeKey = "Theme";
    private const string SettingSortKey = "SortOption";

    private readonly TaskRepository _repository;
    private readonly List<TaskItemViewModel> _allTasks = [];

    private TaskItem? _lastDeletedSnapshot;

    public TaskDetailViewModel Detail { get; }

    public ObservableCollection<TaskListViewModel> CustomLists { get; } = [];
    public ObservableCollection<TaskItemViewModel> ActiveTasks { get; } = [];
    public ObservableCollection<TaskItemViewModel> CompletedTasks { get; } = [];

    public IReadOnlyList<SortOptionItem> SortOptions { get; } =
    [
        new(TaskSortOption.DueDate, "Due Date"),
        new(TaskSortOption.Priority, "Priority"),
        new(TaskSortOption.CreatedNewest, "Newest First"),
        new(TaskSortOption.Title, "Title (A–Z)")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTodaySelected))]
    [NotifyPropertyChangedFor(nameof(IsUpcomingSelected))]
    [NotifyPropertyChangedFor(nameof(IsAllSelected))]
    [NotifyPropertyChangedFor(nameof(IsHighPrioritySelected))]
    [NotifyPropertyChangedFor(nameof(IsCompletedSelected))]
    [NotifyPropertyChangedFor(nameof(IsQuickAddVisible))]
    [NotifyPropertyChangedFor(nameof(ShowSplitCompletedSection))]
    private SmartViewType _activeViewType = SmartViewType.Today;

    [ObservableProperty]
    private TaskListViewModel? _activeCustomList;

    [ObservableProperty]
    private string _viewTitle = "Today";

    [ObservableProperty]
    private string _viewSubtitle = string.Empty;

    [ObservableProperty]
    private string _viewAccentHex = "#3B82F6";

    [ObservableProperty]
    private string _emptyStateTitle = "All clear for today";

    [ObservableProperty]
    private string _emptyStateSubtitle = "Press Ctrl+N or type above to add a task.";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private SortOptionItem _selectedSortOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailPaneOpen))]
    private TaskItemViewModel? _selectedTask;

    [ObservableProperty]
    private bool _isCompletedSectionExpanded = true;

    // Quick-Add fields
    [ObservableProperty]
    private string _quickAddTitle = string.Empty;

    [ObservableProperty]
    private DateOnly? _quickAddDueDate;

    [ObservableProperty]
    private TaskPriority _quickAddPriority = TaskPriority.Normal;

    [ObservableProperty]
    private ListPickerOption? _quickAddListOption;

    public ObservableCollection<ListPickerOption> QuickAddListOptions { get; } = [];

    // Live sidebar counts
    [ObservableProperty] private int _todayCount;
    [ObservableProperty] private int _overdueCount;
    [ObservableProperty] private int _upcomingCount;
    [ObservableProperty] private int _allIncompleteCount;
    [ObservableProperty] private int _highPriorityCount;
    [ObservableProperty] private int _completedCount;

    // Theme & Undo state
    [ObservableProperty]
    private AppThemePreference _themePreference = AppThemePreference.System;

    [ObservableProperty]
    private bool _hasUndoableDelete;

    public bool IsTodaySelected => ActiveViewType == SmartViewType.Today;
    public bool IsUpcomingSelected => ActiveViewType == SmartViewType.Upcoming;
    public bool IsAllSelected => ActiveViewType == SmartViewType.All;
    public bool IsHighPrioritySelected => ActiveViewType == SmartViewType.HighPriority;
    public bool IsCompletedSelected => ActiveViewType == SmartViewType.Completed;
    public bool IsDetailPaneOpen => SelectedTask is not null;
    public bool IsQuickAddVisible => ActiveViewType != SmartViewType.Completed;
    public bool ShowSplitCompletedSection => ActiveViewType != SmartViewType.Completed && CompletedTasks.Count > 0;
    public bool IsCurrentViewEmpty => ActiveViewType == SmartViewType.Completed
        ? CompletedTasks.Count == 0
        : ActiveTasks.Count == 0 && CompletedTasks.Count == 0;

    public event Action<AppThemePreference>? ThemeChanged;
    public event Action<string>? UndoSnackbarRequested;
    public event Action<string>? InfoSnackbarRequested;

    public MainViewModel(TaskRepository repository)
    {
        _repository = repository;
        _selectedSortOption = SortOptions[0];
        Detail = new TaskDetailViewModel(repository, OnDetailMutated, DeleteTaskAsync);
    }

    public async Task InitializeAsync()
    {
        var savedTheme = await _repository.GetSettingAsync(SettingThemeKey);
        if (Enum.TryParse<AppThemePreference>(savedTheme, out var parsedTheme))
        {
            ThemePreference = parsedTheme;
        }
        ThemeChanged?.Invoke(ThemePreference);

        var savedSort = await _repository.GetSettingAsync(SettingSortKey);
        if (Enum.TryParse<TaskSortOption>(savedSort, out var parsedSort))
        {
            SelectedSortOption = SortOptions.FirstOrDefault(s => s.Value == parsedSort) ?? SortOptions[0];
        }

        await ReloadAllDataAsync();
        SelectSmartView(SmartViewType.All);
    }

    public async Task ReloadAllDataAsync()
    {
        var lists = await _repository.GetAllListsAsync();
        CustomLists.Clear();
        foreach (var list in lists)
        {
            CustomLists.Add(new TaskListViewModel(list));
        }

        RefreshListPickers();

        var listLookup = CustomLists.ToDictionary(l => l.Id, l => l.Model);
        var tasks = await _repository.GetAllTasksAsync();
        _allTasks.Clear();
        foreach (var task in tasks)
        {
            var vm = new TaskItemViewModel(task);
            if (task.ListId.HasValue && listLookup.TryGetValue(task.ListId.Value, out var listModel))
            {
                vm.UpdateListMetadata(listModel);
            }
            _allTasks.Add(vm);
        }

        RefreshCountsAndVisibleTasks();
    }

    private void RefreshListPickers()
    {
        Detail.RefreshListOptions(CustomLists);

        var prevQuickId = QuickAddListOption?.Id;
        QuickAddListOptions.Clear();
        QuickAddListOptions.Add(new ListPickerOption(null, "No List", "#64748B"));
        foreach (var list in CustomLists)
        {
            QuickAddListOptions.Add(new ListPickerOption(list.Id, list.Name, list.ColorHex));
        }
        QuickAddListOption = QuickAddListOptions.FirstOrDefault(o => o.Id == prevQuickId)
                             ?? QuickAddListOptions[0];
    }

    // --- Navigation ---

    [RelayCommand]
    private void SelectSmartView(SmartViewType viewType)
    {
        ActiveViewType = viewType;
        ActiveCustomList = null;
        foreach (var list in CustomLists)
        {
            list.IsSelected = false;
        }

        ApplyQuickAddDefaultsForCurrentView();
        UpdateHeaderMetadata();
        RefreshCountsAndVisibleTasks();
    }

    [RelayCommand]
    private void SelectCustomList(TaskListViewModel? listVm)
    {
        if (listVm is null) return;

        ActiveViewType = SmartViewType.CustomList;
        ActiveCustomList = listVm;
        foreach (var list in CustomLists)
        {
            list.IsSelected = list.Id == listVm.Id;
        }

        ApplyQuickAddDefaultsForCurrentView();
        UpdateHeaderMetadata();
        RefreshCountsAndVisibleTasks();
    }

    private void ApplyQuickAddDefaultsForCurrentView()
    {
        QuickAddDueDate = ActiveViewType switch
        {
            SmartViewType.Today => DateOnly.FromDateTime(DateTime.Today),
            SmartViewType.Upcoming => DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            _ => null
        };

        QuickAddPriority = ActiveViewType == SmartViewType.HighPriority
            ? TaskPriority.High
            : TaskPriority.Normal;

        QuickAddListOption = ActiveViewType == SmartViewType.CustomList && ActiveCustomList is not null
            ? QuickAddListOptions.FirstOrDefault(o => o.Id == ActiveCustomList.Id) ?? QuickAddListOptions[0]
            : QuickAddListOptions.FirstOrDefault();
    }

    private void UpdateHeaderMetadata()
    {
        var today = DateTime.Today;
        switch (ActiveViewType)
        {
            case SmartViewType.Today:
                ViewTitle = "Today";
                ViewSubtitle = today.ToString("dddd, MMMM d");
                ViewAccentHex = "#3B82F6";
                EmptyStateTitle = "You're all caught up for today";
                EmptyStateSubtitle = "Tasks due today or overdue appear here. Press Ctrl+N to add one.";
                break;
            case SmartViewType.Upcoming:
                ViewTitle = "Upcoming";
                ViewSubtitle = "Scheduled tasks coming up";
                ViewAccentHex = "#8B5CF6";
                EmptyStateTitle = "No upcoming tasks scheduled";
                EmptyStateSubtitle = "Give any task a future due date to plan ahead.";
                break;
            case SmartViewType.All:
                ViewTitle = "All Tasks";
                ViewSubtitle = "Everything on your plate";
                ViewAccentHex = "#06B6D4";
                EmptyStateTitle = "No tasks yet";
                EmptyStateSubtitle = "Type a task in the quick-add bar above and press Enter.";
                break;
            case SmartViewType.HighPriority:
                ViewTitle = "High Priority";
                ViewSubtitle = "Focus on what matters most";
                ViewAccentHex = "#EF4444";
                EmptyStateTitle = "No high-priority tasks";
                EmptyStateSubtitle = "Mark urgent tasks as High priority to track them here.";
                break;
            case SmartViewType.Completed:
                ViewTitle = "Completed";
                ViewSubtitle = "Finished tasks archive";
                ViewAccentHex = "#10B981";
                EmptyStateTitle = "No completed tasks yet";
                EmptyStateSubtitle = "Check off tasks as you finish them and they'll appear here.";
                break;
            case SmartViewType.CustomList when ActiveCustomList is not null:
                ViewTitle = ActiveCustomList.Name;
                ViewSubtitle = "Custom list";
                ViewAccentHex = ActiveCustomList.ColorHex;
                EmptyStateTitle = $"No tasks in {ActiveCustomList.Name}";
                EmptyStateSubtitle = "Add a task above and it will automatically join this list.";
                break;
        }
    }

    // --- Filtering & Sorting ---

    partial void OnSearchQueryChanged(string value) => RefreshCountsAndVisibleTasks();

    partial void OnSelectedSortOptionChanged(SortOptionItem value)
    {
        RefreshCountsAndVisibleTasks();
        _ = _repository.SetSettingAsync(SettingSortKey, value.Value.ToString());
    }

    partial void OnSelectedTaskChanged(TaskItemViewModel? oldValue, TaskItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }
        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
        _ = Detail.LoadTaskAsync(newValue);
    }

    private void OnDetailMutated()
    {
        RefreshCountsAndVisibleTasks();
    }

    public void RefreshCountsAndVisibleTasks()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        OverdueCount = _allTasks.Count(t => !t.IsComplete && t.DueDate.HasValue && t.DueDate.Value < today);
        TodayCount = _allTasks.Count(t => !t.IsComplete && t.DueDate.HasValue && t.DueDate.Value <= today);
        UpcomingCount = _allTasks.Count(t => !t.IsComplete && t.DueDate.HasValue && t.DueDate.Value > today);
        AllIncompleteCount = _allTasks.Count(t => !t.IsComplete);
        HighPriorityCount = _allTasks.Count(t => !t.IsComplete && t.Priority == TaskPriority.High);
        CompletedCount = _allTasks.Count(t => t.IsComplete);

        foreach (var list in CustomLists)
        {
            list.IncompleteCount = _allTasks.Count(t => !t.IsComplete && t.ListId == list.Id);
        }

        IEnumerable<TaskItemViewModel> matching = ActiveViewType switch
        {
            SmartViewType.Today => _allTasks.Where(t => t.DueDate.HasValue && t.DueDate.Value <= today),
            SmartViewType.Upcoming => _allTasks.Where(t => t.DueDate.HasValue && t.DueDate.Value > today),
            SmartViewType.All => _allTasks,
            SmartViewType.HighPriority => _allTasks.Where(t => t.Priority == TaskPriority.High),
            SmartViewType.Completed => _allTasks.Where(t => t.IsComplete),
            SmartViewType.CustomList when ActiveCustomList is not null =>
                _allTasks.Where(t => t.ListId == ActiveCustomList.Id),
            _ => _allTasks
        };

        var query = SearchQuery.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            matching = matching.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(t.Notes) && t.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.ListName) && t.ListName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                t.Model.Subtasks.Any(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var sorted = ApplySorting(matching).ToList();

        ActiveTasks.Clear();
        CompletedTasks.Clear();

        foreach (var item in sorted)
        {
            if (item.IsComplete)
            {
                CompletedTasks.Add(item);
            }
            else
            {
                ActiveTasks.Add(item);
            }
        }

        OnPropertyChanged(nameof(ShowSplitCompletedSection));
        OnPropertyChanged(nameof(IsCurrentViewEmpty));
    }

    private IEnumerable<TaskItemViewModel> ApplySorting(IEnumerable<TaskItemViewModel> source) =>
        SelectedSortOption.Value switch
        {
            TaskSortOption.DueDate => source
                .OrderBy(t => t.DueDate.HasValue ? 0 : 1)
                .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
                .ThenByDescending(t => (int)t.Priority)
                .ThenByDescending(t => t.CreatedAt),

            TaskSortOption.Priority => source
                .OrderByDescending(t => (int)t.Priority)
                .ThenBy(t => t.DueDate.HasValue ? 0 : 1)
                .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
                .ThenByDescending(t => t.CreatedAt),

            TaskSortOption.CreatedNewest => source
                .OrderByDescending(t => t.CreatedAt),

            TaskSortOption.Title => source
                .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),

            _ => source
        };

    // --- Task CRUD ---

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        var title = QuickAddTitle.Trim();
        if (string.IsNullOrEmpty(title)) return;

        var newTask = new TaskItem
        {
            Title = title,
            DueDate = QuickAddDueDate,
            Priority = QuickAddPriority,
            IsComplete = false,
            CreatedAt = DateTime.UtcNow,
            ListId = QuickAddListOption?.Id
        };

        await _repository.CreateTaskAsync(newTask);

        var vm = new TaskItemViewModel(newTask);
        if (newTask.ListId.HasValue)
        {
            var list = CustomLists.FirstOrDefault(l => l.Id == newTask.ListId.Value);
            vm.UpdateListMetadata(list?.Model);
        }

        _allTasks.Add(vm);
        QuickAddTitle = string.Empty;
        RefreshCountsAndVisibleTasks();
    }

    [RelayCommand]
    private void SetQuickAddDueToday() => QuickAddDueDate = DateOnly.FromDateTime(DateTime.Today);

    [RelayCommand]
    private void SetQuickAddDueTomorrow() => QuickAddDueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

    [RelayCommand]
    private void ClearQuickAddDueDate() => QuickAddDueDate = null;

    [RelayCommand]
    private void CycleQuickAddPriority()
    {
        QuickAddPriority = QuickAddPriority switch
        {
            TaskPriority.Low => TaskPriority.Normal,
            TaskPriority.Normal => TaskPriority.High,
            _ => TaskPriority.Low
        };
    }

    [RelayCommand]
    private async Task ToggleTaskCompleteAsync(TaskItemViewModel? taskVm)
    {
        if (taskVm is null) return;

        taskVm.SyncModelFromProperties();
        await _repository.UpdateTaskAsync(taskVm.Model);
        Detail.SyncCompletionFromList(taskVm);
        RefreshCountsAndVisibleTasks();
    }

    [RelayCommand]
    public async Task DeleteTaskAsync(TaskItemViewModel? taskVm)
    {
        if (taskVm is null) return;

        // Flush any in-flight detail edits before taking undo snapshot
        if (SelectedTask == taskVm)
        {
            await Detail.FlushPendingChangesAsync();
            SelectedTask = null;
        }

        taskVm.SyncModelFromProperties();
        _lastDeletedSnapshot = taskVm.Model.Clone();
        HasUndoableDelete = true;

        await _repository.DeleteTaskAsync(taskVm.Id);
        _allTasks.Remove(taskVm);
        RefreshCountsAndVisibleTasks();

        UndoSnackbarRequested?.Invoke($"Deleted \"{taskVm.Title}\"");
    }

    [RelayCommand]
    public async Task UndoDeleteAsync()
    {
        if (_lastDeletedSnapshot is null) return;

        var snapshot = _lastDeletedSnapshot;
        _lastDeletedSnapshot = null;
        HasUndoableDelete = false;

        var restoredModel = await _repository.RestoreTaskAsync(snapshot);
        var vm = new TaskItemViewModel(restoredModel);
        if (restoredModel.ListId.HasValue)
        {
            var list = CustomLists.FirstOrDefault(l => l.Id == restoredModel.ListId.Value);
            vm.UpdateListMetadata(list?.Model);
        }

        _allTasks.Add(vm);
        RefreshCountsAndVisibleTasks();
        SelectedTask = vm;
        InfoSnackbarRequested?.Invoke($"Restored \"{vm.Title}\"");
    }

    [RelayCommand]
    private void CloseDetailPane()
    {
        SelectedTask = null;
    }

    [RelayCommand]
    private void ToggleCompletedSection()
    {
        IsCompletedSectionExpanded = !IsCompletedSectionExpanded;
    }

    // --- Custom List CRUD ---

    public async Task CreateCustomListAsync(string name, string colorHex, string iconName)
    {
        var list = new TaskList
        {
            Name = name.Trim(),
            ColorHex = colorHex,
            IconName = iconName,
            SortOrder = CustomLists.Count,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.CreateListAsync(list);
        var vm = new TaskListViewModel(list);
        CustomLists.Add(vm);
        RefreshListPickers();
        SelectCustomList(vm);
    }

    public async Task UpdateCustomListAsync(TaskListViewModel listVm, string name, string colorHex, string iconName)
    {
        listVm.Model.Name = name.Trim();
        listVm.Model.ColorHex = colorHex;
        listVm.Model.IconName = iconName;

        await _repository.UpdateListAsync(listVm.Model);
        listVm.SyncFromModel();

        foreach (var task in _allTasks.Where(t => t.ListId == listVm.Id))
        {
            task.UpdateListMetadata(listVm.Model);
        }

        RefreshListPickers();
        if (ActiveCustomList == listVm)
        {
            UpdateHeaderMetadata();
        }
        RefreshCountsAndVisibleTasks();
    }

    [RelayCommand]
    public async Task DeleteCustomListAsync(TaskListViewModel? listVm)
    {
        if (listVm is null) return;

        await _repository.DeleteListAsync(listVm.Id);
        CustomLists.Remove(listVm);

        foreach (var task in _allTasks.Where(t => t.ListId == listVm.Id))
        {
            task.ListId = null;
            task.Model.ListId = null;
            task.UpdateListMetadata(null);
        }

        RefreshListPickers();

        if (ActiveCustomList == listVm)
        {
            SelectSmartView(SmartViewType.All);
        }
        else
        {
            RefreshCountsAndVisibleTasks();
        }

        InfoSnackbarRequested?.Invoke($"Deleted list \"{listVm.Name}\" (tasks kept in All Tasks)");
    }

    // --- Theme ---

    [RelayCommand]
    private async Task SetThemeAsync(AppThemePreference preference)
    {
        ThemePreference = preference;
        ThemeChanged?.Invoke(preference);
        await _repository.SetSettingAsync(SettingThemeKey, preference.ToString());
    }
}
