using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hatch.Converters;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.Views;

namespace Hatch.ViewModels;

public sealed class StatsViewModel : INotifyPropertyChanged
{
    // Reused from the nav rail (MainPage.xaml) so a tile's icon matches the destination
    // it navigates to: Sun = My Day, Calendar = Planned (Due today routes there too),
    // Star = Important. StarredGlyph (U+E735) is the filled star, per BoolToStarGlyphConverter's
    // "starred" state — since this tile counts starred items, not the category itself.
    private const string MyDayGlyph    = "\uE706";
    private const string DueTodayGlyph = "\uED28";
    private const string OverdueGlyph  = "\uE7BA";
    private const string StarredGlyph  = "\uE735";

    private static readonly Windows.Globalization.DateTimeFormatting.DateTimeFormatter _dueLabelFormatter =
        new("dayofweek.abbreviated month.abbreviated day");

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
        // Icons sit inline on the title's line now, so the colour has to carry the tile's
        // state on its own — there is no longer a badge fill behind it.
        var neutralFg  = ThemeResourceHelper.GetBrush("TextFillColorSecondaryBrush");
        var successFg  = ThemeResourceHelper.GetBrush("SystemFillColorSuccessBrush");
        var criticalFg = ThemeResourceHelper.GetBrush("SystemFillColorCriticalBrush");
        // No standard "starred/gold" theme token in this app's palette — same light/dark
        // hardcoded pairing PriorityToForegroundConverter already uses for chip colors,
        // via the ThemeResourceHelper.GetThemedBrush overload built for exactly this.
        var starredFg = ThemeResourceHelper.GetThemedBrush(
            Windows.UI.Color.FromArgb(255, 157, 108,   0),
            Windows.UI.Color.FromArgb(255, 255, 200,  87));

        var tasks = Tasks.ToList();
        var today = DateTime.Today;

        // completed/total, not remaining/total: the percentage beneath it measures
        // completion, so the fraction has to agree — remaining/total would render an
        // untouched day as "2 / 2" against "0%". Completing a task does not clear
        // IsInMyDay (only the daily reset does), so finished tasks stay in the
        // denominator, which is what makes this a progress ratio at all.
        int myDayTotal     = tasks.Count(t => t.IsInMyDay);
        int myDayCompleted = tasks.Count(t => t.IsInMyDay && t.IsCompleted);
        int myDayPercent   = myDayTotal > 0 ? (int)Math.Round(myDayCompleted * 100.0 / myDayTotal) : 0;
        bool myDayPlanned  = myDayTotal > 0;

        // Due dates: the day as written, never through ToLocalTime — see TipEngine.
        int dueToday = tasks.Count(t =>
            !t.IsCompleted && t.DueDate != null && t.DueDate.Value.Date == today);

        int overdue = tasks.Count(t =>
            !t.IsCompleted && t.DueDate != null && t.DueDate.Value.Date < today);

        // Matches the Important nav filter (MainViewModel.Filtering.cs): starred and not
        // yet completed — a star never clears itself, so a completed task would otherwise
        // sit in this count forever.
        int starred = tasks.Count(t => t.IsStarred && !t.IsCompleted);

        Tiles.Clear();
        // An empty My Day has no ratio to report: "0 / 0" against "0%" in success-green
        // reads as failure when the truth is that nothing has been planned yet. Drop to a
        // plain count, a neutral icon, and a description that says so.
        Tiles.Add(new StatTileInfo(
            "Stats_MyDay", Strings.Stats_Tile_MyDay_Title,
            myDayPlanned ? $"{myDayCompleted} / {myDayTotal}" : "0",
            myDayPlanned ? $"{myDayPercent}%" : null,
            myDayPlanned ? Strings.Stats_Tile_MyDay_Description : Strings.Stats_Tile_MyDay_Description_Empty,
            MyDayGlyph,
            myDayPlanned ? successFg : neutralFg,
            "myday"));
        Tiles.Add(new StatTileInfo(
            "Stats_DueToday", Strings.Stats_Tile_DueToday_Title,
            dueToday.ToString(), null,
            Strings.Stats_Tile_DueToday_Description,
            DueTodayGlyph, neutralFg, "planned"));
        Tiles.Add(new StatTileInfo(
            "Stats_Overdue", Strings.Stats_Tile_Overdue_Title,
            overdue.ToString(), null,
            overdue > 0 ? Strings.Stats_Tile_Overdue_Description_Active : Strings.Stats_Tile_Overdue_Description_Clear,
            OverdueGlyph, overdue > 0 ? criticalFg : neutralFg, "planned"));
        Tiles.Add(new StatTileInfo(
            "Stats_Starred", Strings.Stats_Tile_Starred_Title,
            starred.ToString(), null,
            Strings.Stats_Tile_Starred_Description,
            StarredGlyph, starredFg, "important"));

        var tomorrow = today.AddDays(1);

        // Split into "Today" (the PM's daily-agenda glance — what's actually due right now)
        // and "Upcoming" (strictly future, forward-planning). Deliberately not a calendar
        // view — reuses the due-date data already on hand instead of duplicating Planned.
        var dueTodayTasks = tasks
            .Where(t => !t.IsCompleted && t.DueDate != null && t.DueDate.Value.Date == today)
            .OrderBy(TaskSorting.CreatedInstant);

        TodayTasks.Clear();
        foreach (var task in dueTodayTasks)
            TodayTasks.Add(new UpcomingTaskInfo(task, task.Title, task.ListName));
        HasTodayTasks = TodayTasks.Count > 0;

        var upcoming = tasks
            .Where(t => !t.IsCompleted && t.DueDate != null && t.DueDate.Value.Date > today)
            .OrderBy(t => t.DueDate)
            .Take(5);

        UpcomingTasks.Clear();
        foreach (var task in upcoming)
        {
            var dueDate = task.DueDate!.Value.Date;
            var dueLabel = dueDate == tomorrow ? Strings.DueDate_Tomorrow : _dueLabelFormatter.Format(dueDate);
            UpcomingTasks.Add(new UpcomingTaskInfo(task, task.Title, dueLabel));
        }
        HasUpcomingTasks = UpcomingTasks.Count > 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
