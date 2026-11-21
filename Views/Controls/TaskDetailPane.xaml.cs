using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskTracker2.ViewModels;

namespace TaskTracker2.Views.Controls;

public partial class TaskDetailPane : UserControl
{
    public TaskDetailPane()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.CloseDetailPaneCommand.Execute(null);
        }
    }

    private async void NewSubtaskTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            e.Handled = true;
            await vm.Detail.AddSubtaskCommand.ExecuteAsync(null);
        }
    }

    private async void SubtaskCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm &&
            sender is CheckBox { DataContext: SubtaskItemViewModel subVm })
        {
            await vm.Detail.ToggleSubtaskCommand.ExecuteAsync(subVm);
        }
    }

    private async void DeleteSubtaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm &&
            sender is FrameworkElement { DataContext: SubtaskItemViewModel subVm })
        {
            await vm.Detail.DeleteSubtaskCommand.ExecuteAsync(subVm);
        }
    }
}
