using System.Windows;
using TaskTracker2.Data;
using TaskTracker2.ViewModels;
using TaskTracker2.Views;

namespace TaskTracker2;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var dbPath = DatabasePaths.GetDefaultDatabasePath();
            var initializer = new DatabaseInitializer(dbPath);
            await initializer.InitializeAsync();

            var repository = new TaskRepository(dbPath);
            var mainViewModel = new MainViewModel(repository);

            var mainWindow = new MainWindow(mainViewModel);
            MainWindow = mainWindow;
            mainWindow.Show();

            await mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize TaskTracker:\n\n{ex.Message}",
                "TaskTracker Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
