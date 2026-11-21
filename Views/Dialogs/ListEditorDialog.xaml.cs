using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskTracker2.ViewModels;
using Wpf.Ui.Controls;

namespace TaskTracker2.Views.Dialogs;

public partial class ListEditorDialog : FluentWindow
{
    public ListEditorDialogViewModel ViewModel { get; }

    public ListEditorDialog(ListEditorDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            SyncInitialSelections();
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void SyncInitialSelections()
    {
        foreach (var rb in FindVisualChildren<RadioButton>(this))
        {
            if (rb.GroupName == "ListColorGroup" && rb.Tag is string hex &&
                string.Equals(hex, ViewModel.SelectedColorHex, StringComparison.OrdinalIgnoreCase))
            {
                rb.IsChecked = true;
            }
            else if (rb.GroupName == "ListIconGroup" && rb.Tag is string icon &&
                     string.Equals(icon, ViewModel.SelectedIconName, StringComparison.OrdinalIgnoreCase))
            {
                rb.IsChecked = true;
            }
        }
    }

    private void ColorSwatch_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string hex })
        {
            ViewModel.SelectedColorHex = hex;
        }
    }

    private void IconOption_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string icon })
        {
            ViewModel.SelectedIconName = icon;
        }
    }

    private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel.CanSave)
        {
            DialogResult = true;
            Close();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSave) return;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }
            foreach (var grandChild in FindVisualChildren<T>(child))
            {
                yield return grandChild;
            }
        }
    }
}
