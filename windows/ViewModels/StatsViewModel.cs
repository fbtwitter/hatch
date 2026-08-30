using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Hatch.Converters;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.Views;
using Microsoft.UI.Xaml.Media;

namespace Hatch.ViewModels;

public sealed class StatsViewModel : INotifyPropertyChanged
{
    // Reused from the nav rail (MainPage.xaml) so a tile's icon matches the destination
    // it navigates to: Sun = My Day, Calendar = Planned (Due today routes there too),
    // Star = Important. StarredGlyph (U+E735) is the filled star, per BoolToStarGlyphConverter's
    // "starred" state — since this tile counts starred items, not the category itself.
    private const string MyDayGlyph    = "";
    private const string DueTodayGlyph = "";
    private const string OverdueGlyph  = "";
    private const string StarredGlyph  = "";

    // "This week" strip: the tallest bar is this many pixels, the shortest (a day with a
    // completion but not the peak, or zero) never drops below the floor so the row stays
    // legible as a row rather than a scatter of dots.
    private const double RhythmBand  = 56;
    private const double RhythmFloor = 6;

    private static readonly Windows.Globalization.DateTimeFormatting.DateTimeFormatter _dueLabelFormatter =
        new("dayofweek.abbreviated month.abbreviated day");

    public ObservableCollection<StatTileInfo> Tiles { get; } = [];
    public ObservableCollection<UpcomingTaskInfo> TodayTasks { get; } = [];
    public ObservableCollection<UpcomingTaskInfo> UpcomingTasks { get; } = [];
    public ObservableCollection<RhythmBarInfo> WeekRhythm { get; } = [];

    private MyDayHeroInfo _myDay = new(false, 0, "0", "My Day", "", MyDayGlyph, new SolidColorBrush());
    public MyDayHeroInfo MyDay
    {
        get => _myDay;
        private set { _myDay = value; OnPropertyChanged(); }
    }

    private string _weekTotalText = "";
    public string WeekTotalText
    {
        get => _weekTotalText;
        private set { _weekTotalText = value; OnPropertyChanged(); }
    }

    private string? _streakText;
    public string? StreakText
    {
        get => _streakText;
        private set { _streakText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStreak)); }
    }

    public bool HasStreak => !string.IsNullOrEmpty(_streakText);

    private string _weekRhythmAutomationName = "";
    public string WeekRhythmAutomationName
    {
        get => _weekRhythmAutomationName;
        private set { _weekRhythmAutomationName = value; OnPropertyChanged(); }
    }

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
        var neutralFg  = ThemeResourceHelper.GetBrush("TextFillColorSecondaryBrush");
        var successFg  = ThemeResourceHelper.GetBrush("SystemFillColorSuccessBrush");
        var criticalFg = ThemeResourceHelper.GetBrush("SystemFillColorCriticalBrush");
        // No standard "starred/gold" theme token in this app's palette — same light/dark
        // hardcoded pairing PriorityToForegroundConverter already uses for chip colors.
        var starredFg = ThemeResourceHelper.GetThemedBrush(
            Windows.UI.Color.FromArgb(255, 157, 108,   0),
            Windows.UI.Color.FromArgb(255, 255, 200,  87));

        var neutralBg  = ThemeResourceHelper.GetBrush("CardBackgroundFillColorDefaultBrush");
        var successBg   = ThemeResourceHelper.GetBrush("SystemFillColorSuccessBackgroundBrush");
        var criticalBg  = ThemeResourceHelper.GetBrush("SystemFillColorCriticalBackgroundBrush");
        // A low-alpha gold wash — same hues as starredFg, thinned to a tile-fill tint.
        var starredBg = ThemeResourceHelper.GetThemedBrush(
            Windows.UI.Color.FromArgb(38, 157, 108,   0),
            Windows.UI.Color.FromArgb(38, 255, 200,  87));

        var tasks = Tasks.ToList();
        var today = DateTime.Today;

        // completed/total, not remaining/total: the percentage measures completion, so the
        // fraction has to agree — remaining/total would render an untouched day as "2 / 2"
        // against "0%". Completing a task does not clear IsInMyDay (only the daily reset
        // does), so finished tasks stay in the denominator.
        int myDayTotal     = tasks.Count(t => t.IsInMyDay);
        int myDayCompleted = tasks.Count(t => t.IsInMyDay && t.IsCompleted);
        int myDayPercent   = myDayTotal > 0 ? (int)Math.Round(myDayCompleted * 100.0 / myDayTotal) : 0;
        bool myDayPlanned  = myDayTotal > 0;

        MyDay = new MyDayHeroInfo(
            myDayPlanned,
            myDayPercent,
            myDayPlanned ? $"{myDayPercent}%" : "0",
            Strings.Stats_Tile_MyDay_Title,
            myDayPlanned
                ? Strings.Stats_MyDay_Hero_Done(myDayCompleted, myDayTotal)
                : Strings.Stats_Tile_MyDay_Description_Empty,
            MyDayGlyph,
            myDayPlanned ? successBg : neutralBg);

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
        Tiles.Add(new StatTileInfo(
            "Stats_DueToday", Strings.Stats_Tile_DueToday_Title,
            dueToday.ToString(),
            Strings.Stats_Tile_DueToday_Description,
            DueTodayGlyph, neutralFg, neutralBg, "planned"));
        Tiles.Add(new StatTileInfo(
            "Stats_Overdue", Strings.Stats_Tile_Overdue_Title,
            overdue.ToString(),
            overdue > 0 ? Strings.Stats_Tile_Overdue_Description_Active : Strings.Stats_Tile_Overdue_Description_Clear,
            OverdueGlyph,
            overdue > 0 ? criticalFg : neutralFg,
            overdue > 0 ? criticalBg : neutralBg,
            "planned"));
        Tiles.Add(new StatTileInfo(
            "Stats_Starred", Strings.Stats_Tile_Starred_Title,
            starred.ToString(),
            Strings.Stats_Tile_Starred_Description,
            StarredGlyph, starredFg, starredBg, "important"));

        RefreshWeekRhythm(tasks, today);

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

    private void RefreshWeekRhythm(IReadOnlyList<TodoItem> tasks, DateTime today)
    {
        var accentFill  = ThemeResourceHelper.GetBrush("AccentFillColorDefaultBrush");
        var partialFill = ThemeResourceHelper.GetBrush("AccentFillColorSecondaryBrush");
        var emptyFill   = ThemeResourceHelper.GetBrush("ControlFillColorSecondaryBrush");
        var todayLabel  = ThemeResourceHelper.GetBrush("AccentTextFillColorPrimaryBrush");
        var normalLabel = ThemeResourceHelper.GetBrush("TextFillColorSecondaryBrush");

        var days = SummaryStats.WeekRhythm(tasks, today);
        int peak = Math.Max(1, days.Max(d => d.Completed));
        var dayNames = CultureInfo.CurrentCulture.DateTimeFormat;

        WeekRhythm.Clear();
        foreach (var day in days)
        {
            double fraction = (double)day.Completed / peak;
            var fill = day.IsToday ? accentFill : day.Completed > 0 ? partialFill : emptyFill;
            WeekRhythm.Add(new RhythmBarInfo(
                RhythmFloor + (RhythmBand - RhythmFloor) * fraction,
                fill,
                dayNames.GetAbbreviatedDayName(day.Date.DayOfWeek)[..1],
                day.IsToday ? todayLabel : normalLabel,
                day.IsToday));
        }

        int total = days.Sum(d => d.Completed);
        WeekTotalText = total == 0 ? Strings.Stats_Week_NothingDone : Strings.Stats_Week_Done(total);

        int streak = SummaryStats.CurrentStreak(tasks, today);
        // A 1-day streak is just "today" — not worth naming until it's actually a run.
        StreakText = streak >= 2 ? Strings.Stats_Week_Streak(streak) : null;

        WeekRhythmAutomationName = Strings.Stats_Week_RhythmName(total)
            + (StreakText is { } s ? $", {s}" : "");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
