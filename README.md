# TaskTracker2

A modern Windows 11 Fluent desktop task tracker built with **C#**, **WPF (.NET 10)**, **WPF-UI**, and **SQLite**. Designed with a clean 3-pane master-detail workspace inspired by Microsoft To Do, with local-first storage, custom lists, subtasks, keyboard shortcuts, theme switching, and undo support.

## Features

- **Windows 11 Fluent UI**: Native Mica backdrop, rounded cards, Fluent icons, and live **Auto / Light / Dark** theme switching (persisted locally).
- **3-Pane Workspace**:
  - **Sidebar**: Smart views (*Today* with overdue badge, *Upcoming*, *All Tasks*, *High Priority*, *Completed*) with live counters, plus user-defined **Custom Lists** with accent colors and icons.
  - **Task List & Quick-Add**: Fast single-line task creation (`Ctrl+N`) with inline due date presets, priority cycler (`Low`, `Normal`, `High`), list assignment, instant search (`Ctrl+F`), sorting (*Due Date*, *Priority*, *Newest First*, *Title*), and a collapsible Completed section.
  - **Slide-In Detail Pane**: Edit task title and notes with debounced auto-save, quick due-date chips (*Today*, *Tomorrow*, *Next Week*, *Clear*) + calendar picker, segmented priority selector, list assignment, and an inline **Subtasks** checklist.
- **Undo Delete**: Deleting a task displays a 6-second floating snackbar with an **Undo** button (`Ctrl+Z` also restores the task and all of its subtasks).
- **Local SQLite Storage**: Stores data in `%LOCALAPPDATA%\TaskTracker\tasks.db` with safe, additive, idempotent schema migrations that preserve backward compatibility with an existing `Tasks` table.

## Tech Stack

- **.NET 10** (`net10.0-windows`, WPF)
- **[WPF-UI](https://github.com/lepoco/wpfui)** — Windows 11 Fluent controls, Mica backdrop, and theme management
- **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)** — Source-generated MVVM (`ObservableObject`, `RelayCommand`)
- **[Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)** — Lightweight async SQLite data access
- **xUnit** — Automated migration and repository tests

## Keyboard Shortcuts

- `Ctrl+N` — Focus the Quick-Add bar
- `Ctrl+F` — Focus the Search box
- `Enter` — Add task (in Quick-Add) or add subtask (in Detail Pane)
- `Delete` — Delete the selected task (when not typing in a text field)
- `Ctrl+Z` — Undo the last task deletion (when not typing in a text field)
- `Esc` — Clear search (when focused) or close the Detail Pane

## Database Schema

The database is stored at `%LOCALAPPDATA%\TaskTracker\tasks.db` and initialized automatically on first launch.

- `Tasks` (`Id`, `Title`, `Notes`, `DueDate` as `yyyy-MM-dd`, `Priority` as `1 = Low`, `2 = Normal`, `3 = High`, `IsComplete`, `CreatedAt` ISO-8601 UTC, nullable `ListId`)
- `TaskLists` (`Id`, `Name`, `ColorHex`, `IconName`, `SortOrder`, `CreatedAt`)
- `Subtasks` (`Id`, `TaskId` FK with cascade delete, `Title`, `IsComplete`, `SortOrder`)
- `AppSettings` (`Key`, `Value`)

## Getting Started

### Prerequisites

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or newer with the .NET 10 Windows Desktop runtime)

### Run the App

```powershell
git clone https://github.com/krazykyran/WPF-TaskTracker2.git
cd WPF-TaskTracker2
dotnet run
```

### Run Tests

The test suite runs against temporary copies of the database and never modifies your live `tasks.db`:

```powershell
dotnet test TaskTracker2.sln
```

## Project Structure

```text
TaskTracker2/
├── App.xaml / App.xaml.cs          # Fluent theme dictionaries & startup wiring
├── Models/                         # TaskItem, TaskList, SubtaskItem, TaskPriority, SmartViewType
├── Data/                           # DatabasePaths, DatabaseInitializer, TaskRepository
├── ViewModels/                     # MainViewModel, TaskItemViewModel, TaskDetailViewModel, ListEditorDialogViewModel
├── Views/
│   ├── MainWindow.xaml             # 3-pane FluentWindow, shortcuts, floating undo snackbar
│   ├── Controls/                   # SidebarView, TaskListView, TaskDetailPane
│   └── Dialogs/                    # ListEditorDialog
├── Converters/                     # Value converters for priority, dates, colors, and visibility
└── TaskTracker2.Tests/             # xUnit migration & repository tests
```

## License

This project is licensed under the [MIT License](LICENSE).
