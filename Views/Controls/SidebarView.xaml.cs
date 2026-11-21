using System.Windows;
using System.Windows.Controls;
using TaskTracker2.ViewModels;
using TaskTracker2.Views.Dialogs;

namespace TaskTracker2.Views.Controls;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
    }

    private async void AddListButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var dialogVm = new ListEditorDialogViewModel();
        var dialog = new ListEditorDialog(dialogVm)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await vm.CreateCustomListAsync(dialogVm.Name, dialogVm.SelectedColorHex, dialogVm.SelectedIconName);
        }
    }

    private async void EditListMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not MenuItem { DataContext: TaskListViewModel listVm }) return;

        var dialogVm = new ListEditorDialogViewModel(listVm.Model);
        var dialog = new ListEditorDialog(dialogVm)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await vm.UpdateCustomListAsync(listVm, dialogVm.Name, dialogVm.SelectedColorHex, dialogVm.SelectedIconName);
        }
    }

    private async void DeleteListMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not MenuItem { DataContext: TaskListViewModel listVm }) return;

        await vm.DeleteCustomListAsync(listVm);
    }
}
