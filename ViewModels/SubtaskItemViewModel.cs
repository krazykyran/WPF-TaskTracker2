using CommunityToolkit.Mvvm.ComponentModel;
using TaskTracker2.Models;

namespace TaskTracker2.ViewModels;

public partial class SubtaskItemViewModel : ObservableObject
{
    public SubtaskItem Model { get; }

    public long Id => Model.Id;
    public long TaskId => Model.TaskId;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isComplete;

    public SubtaskItemViewModel(SubtaskItem model)
    {
        Model = model;
        _title = model.Title;
        _isComplete = model.IsComplete;
    }
}
