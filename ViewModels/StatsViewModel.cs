using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hatch.Converters;
using Hatch.Models;
using Hatch.Views;

namespace Hatch.ViewModels;

public sealed class StatsViewModel : INotifyPropertyChanged
{
    private const string OverdueGlyph = "";
    private const string CheckGlyph   = "";
    private const string ListGlyph    = "";

    public ObservableCollection<StatTileInfo> Tiles { get; } = [];
    public ObservableCollection<UpcomingTaskInfo> TodayTasks { get; } = [];
    public ObservableCollection<UpcomingTaskInfo> UpcomingTasks { get; } = [];

    private bool _hasTodayTasks;
    public bool HasTodayTasks
    {
        get => _hasTodayTasks;
        private set { _hasTodayTasks = value; OnPropertyChanged(); }
    }

    private bool _hasUpcomingTasks;
    public bool HasUpcomingTasks
    {
        get => _hasUpcomingTasks;
        private set { _hasUpcomingTasks = value; OnPropertyChanged(); }
    }

    private IEnumerable<TodoItem> Tasks =>
        (App.MainWindowInstance as MainWindow)?.ViewModel.Tasks ?? Enumerable.Empty<TodoItem>();

    // Called from StatsPage.OnNavigatedTo — computed on demand, no live binding needed
    // since the task list and this page can't be visible at the same time.
    public void RefreshStats()
    {
        var neutralBg  = ThemeResourceHelper.GetBrush("SubtleFillColorSecondaryBrush");
        var neutralFg  = ThemeResourceHelper.GetBrush("TextFillColorSecondaryBrush");
        var successBg  = ThemeResourceHelper.GetBrush("SystemFillColorSuccessBackgroundBrush");
        var successFg  = ThemeResourceHelper.GetBrush("SystemFillColorSuccessBrush");
        var criticalBg = ThemeResourceHelper.GetBrush("SystemFillColorCriticalBackgroundBrush");
        var criticalFg = ThemeResourceHelper.GetBrush("SystemFillColorCriticalBrush");

        var tasks = Tasks.ToList();
        var today = DateTime.Today;

        int open = tasks.Count(t => !t.IsCompleted);
        int overdue = tasks.Count(t =>
            !t.IsCompleted && t.DueDate != null && t.DueDate.Value.ToLocalTime().Date < today);
        int completedToday = tasks.Count(t =>
            t.CompletedAt.HasValue && t.CompletedAt.Value.ToLocalTime().Date == today);

        Tiles.Clear();
        Tiles.Add(new StatTileInfo("Stats_Overdue", overdue, "Overdue", OverdueGlyph, criticalFg, criticalBg));
        Tiles.Add(new StatTileInfo("Stats_CompletedToday", completedToday, "Completed today", CheckGlyph, successFg, successBg));
        Tiles.Add(new StatTileInfo("Stats_Open", open, "Open", ListGlyph, neutralFg, neutralBg));

        var tomorrow = today.AddDays(1);

        // Split into "Today" (the PM's daily-agenda glance — what's actually due right now)
        // and "Upcoming" (strictly future, forward-planning). Deliberately not a calendar
        // view — reuses the due-date data already on hand instead of duplicating Planned.
        var dueToday = tasks
            .Where(t => !t.IsCompleted && t.DueDate != null && t.DueDate.Value.ToLocalTime().Date == today)
            .OrderBy(t => t.CreatedAt);

        TodayTasks.Clear();
        foreach (var task in dueToday)
            TodayTasks.Add(new UpcomingTaskInfo(task.Title, task.ListName ?? "Task", task.ListName));
        HasTodayTasks = TodayTasks.Count > 0;

        var upcoming = tasks
            .Where(t => !t.IsCompleted && t.DueDate != null && t.DueDate.Value.ToLocalTime().Date > today)
            .OrderBy(t => t.DueDate)
            .Take(5);

        UpcomingTasks.Clear();
        foreach (var task in upcoming)
        {
            var dueDate = task.DueDate!.Value.ToLocalTime().Date;
            var dueLabel = dueDate == tomorrow ? "Tomorrow" : dueDate.ToString("ddd, MMM d");
            UpcomingTasks.Add(new UpcomingTaskInfo(task.Title, dueLabel, task.ListName));
        }
        HasUpcomingTasks = UpcomingTasks.Count > 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
