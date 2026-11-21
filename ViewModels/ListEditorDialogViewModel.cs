using CommunityToolkit.Mvvm.ComponentModel;
using TaskTracker2.Models;

namespace TaskTracker2.ViewModels;

public sealed record ColorSwatchOption(string Name, string Hex);
public sealed record IconPickerOption(string Label, string SymbolName);

public partial class ListEditorDialogViewModel : ObservableObject
{
    public IReadOnlyList<ColorSwatchOption> ColorOptions { get; } =
    [
        new("Blue", "#3B82F6"),
        new("Indigo", "#6366F1"),
        new("Violet", "#8B5CF6"),
        new("Pink", "#EC4899"),
        new("Red", "#EF4444"),
        new("Amber", "#F59E0B"),
        new("Emerald", "#10B981"),
        new("Cyan", "#06B6D4")
    ];

    public IReadOnlyList<IconPickerOption> IconOptions { get; } =
    [
        new("List", "List24"),
        new("Home", "Home24"),
        new("Work", "Briefcase24"),
        new("Shopping", "Cart24"),
        new("Star", "Star24"),
        new("Personal", "Heart24"),
        new("Study", "Book24"),
        new("Ideas", "Lightbulb24"),
        new("Tools", "Wrench24"),
        new("Launch", "Rocket24")
    ];

    public bool IsEditing { get; }
    public string DialogTitle => IsEditing ? "Edit List" : "New List";
    public string PrimaryButtonText => IsEditing ? "Save" : "Create";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _selectedColorHex = "#3B82F6";

    [ObservableProperty]
    private string _selectedIconName = "List24";

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    public ListEditorDialogViewModel(TaskList? existing = null)
    {
        if (existing is not null)
        {
            IsEditing = true;
            Name = existing.Name;
            SelectedColorHex = existing.ColorHex;
            SelectedIconName = existing.IconName;
        }
    }
}
