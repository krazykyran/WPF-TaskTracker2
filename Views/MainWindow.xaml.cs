using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TaskTracker2.Models;
using TaskTracker2.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TaskTracker2.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _toastTimer;
    private bool _isWatchingSystemTheme;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        _toastTimer.Tick += (_, _) => HideToast();

        _viewModel.ThemeChanged += ApplyTheme;
        _viewModel.UndoSnackbarRequested += msg => ShowToast(msg, showUndo: true);
        _viewModel.InfoSnackbarRequested += msg => ShowToast(msg, showUndo: false);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Closing += MainWindow_Closing;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDetailPaneOpen))
        {
            UpdateDetailPaneColumnState();
        }
        else if (e.PropertyName == nameof(MainViewModel.DetailPaneWidth) && _viewModel.IsDetailPaneOpen)
        {
            DetailPaneColumn.Width = new GridLength(_viewModel.DetailPaneWidth);
        }
    }

    private void UpdateDetailPaneColumnState()
    {
        if (_viewModel.IsDetailPaneOpen)
        {
            DetailPaneColumn.MinWidth = MainViewModel.MinDetailPaneWidth;
            DetailPaneColumn.MaxWidth = MainViewModel.MaxDetailPaneWidth;
            DetailPaneColumn.Width = new GridLength(_viewModel.DetailPaneWidth);
        }
        else
        {
            DetailPaneColumn.MinWidth = 0;
            DetailPaneColumn.MaxWidth = double.PositiveInfinity;
            DetailPaneColumn.Width = new GridLength(0);
        }
    }

    private async void DetailSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_viewModel.IsDetailPaneOpen && DetailPaneColumn.ActualWidth > 0)
        {
            await _viewModel.SaveDetailPaneWidthAsync(DetailPaneColumn.ActualWidth);
        }
    }

    private void ApplyTheme(AppThemePreference preference)
    {
        if (_isWatchingSystemTheme)
        {
            SystemThemeWatcher.UnWatch(this);
            _isWatchingSystemTheme = false;
        }

        switch (preference)
        {
            case AppThemePreference.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                break;

            case AppThemePreference.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
                break;

            case AppThemePreference.System:
            default:
                ApplicationThemeManager.ApplySystemTheme(true);
                SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);
                _isWatchingSystemTheme = true;
                break;
        }
    }

    private void ShowToast(string message, bool showUndo)
    {
        _toastTimer.Stop();
        ToastMessageText.Text = message;
        ToastUndoButton.Visibility = showUndo ? Visibility.Visible : Visibility.Collapsed;
        ToastIcon.Symbol = showUndo ? SymbolRegular.Delete20 : SymbolRegular.CheckmarkCircle20;
        ToastBanner.Visibility = Visibility.Visible;
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        ToastBanner.Visibility = Visibility.Collapsed;
    }

    private async void ToastUndoButton_Click(object sender, RoutedEventArgs e)
    {
        HideToast();
        await _viewModel.UndoDeleteAsync();
    }

    private void ToastDismissButton_Click(object sender, RoutedEventArgs e)
    {
        HideToast();
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var isTypingInTextBox = Keyboard.FocusedElement is TextBoxBase;

        if (modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            e.Handled = true;
            if (!_viewModel.IsQuickAddVisible)
            {
                _viewModel.SelectSmartViewCommand.Execute(SmartViewType.All);
            }
            TaskListControl.FocusQuickAdd();
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            e.Handled = true;
            TaskListControl.FocusSearch();
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.Z && !isTypingInTextBox && _viewModel.HasUndoableDelete)
        {
            e.Handled = true;
            HideToast();
            await _viewModel.UndoDeleteAsync();
            return;
        }

        if (e.Key == Key.Delete && modifiers == ModifierKeys.None && !isTypingInTextBox && _viewModel.SelectedTask is not null)
        {
            e.Handled = true;
            await _viewModel.DeleteTaskAsync(_viewModel.SelectedTask);
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (TaskListControl.IsSearchFocused && !string.IsNullOrEmpty(_viewModel.SearchQuery))
            {
                e.Handled = true;
                _viewModel.SearchQuery = string.Empty;
            }
            else if (_viewModel.IsDetailPaneOpen)
            {
                e.Handled = true;
                _viewModel.CloseDetailPaneCommand.Execute(null);
            }
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Flush any debounced text edits synchronously before exit
        _viewModel.Detail.FlushPendingChangesAsync().GetAwaiter().GetResult();
    }
}
