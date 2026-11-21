using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskTracker2.ViewModels;

namespace TaskTracker2.Views.Controls;

public partial class TaskListView : UserControl
{
    public TaskListView()
    {
        InitializeComponent();
    }

    public void FocusQuickAdd()
    {
        if (QuickAddTextBox.IsVisible)
        {
            QuickAddTextBox.Focus();
            QuickAddTextBox.SelectAll();
        }
    }

    public void FocusSearch()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    public bool IsSearchFocused => SearchTextBox.IsKeyboardFocusWithin;

    private async void QuickAddTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            e.Handled = true;
            await vm.AddTaskCommand.ExecuteAsync(null);
        }
    }

    private void TaskCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is FrameworkElement { DataContext: TaskItemViewModel taskVm })
        {
            vm.SelectedTask = taskVm;
        }
    }

    private void TaskCheckBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Prevent card click from firing simultaneously when toggling the checkbox
    }

    private async void TaskCheckBox_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel vm) return;
        if (sender is CheckBox { DataContext: TaskItemViewModel taskVm })
        {
            await vm.ToggleTaskCompleteCommand.ExecuteAsync(taskVm);
        }
    }

    private async void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel vm) return;
        if (sender is FrameworkElement { DataContext: TaskItemViewModel taskVm })
        {
            await vm.DeleteTaskAsync(taskVm);
        }
    }
}
