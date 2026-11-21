using CommunityToolkit.Mvvm.ComponentModel;
using TaskTracker2.Models;

namespace TaskTracker2.ViewModels;

public partial class TaskListViewModel : ObservableObject
{
    public TaskList Model { get; }

    public long Id => Model.Id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _colorHex;

    [ObservableProperty]
    private string _iconName;

    [ObservableProperty]
    private int _incompleteCount;

    [ObservableProperty]
    private bool _isSelected;

    public TaskListViewModel(TaskList model)
    {
        Model = model;
        _name = model.Name;
        _colorHex = model.ColorHex;
        _iconName = model.IconName;
    }

    public void SyncFromModel()
    {
        Name = Model.Name;
        ColorHex = Model.ColorHex;
        IconName = Model.IconName;
    }
}
