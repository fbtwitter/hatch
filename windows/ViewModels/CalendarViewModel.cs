using System.Collections.ObjectModel;
using System.ComponentModel;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Hatch.ViewModels;

public sealed partial class CalendarViewModel : INotifyPropertyChanged
{
    private readonly CalendarService _service;
    private readonly Func<Task<string?>>? _pickImportFile;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _clock;
    private List<CalendarEvent> _shownEvents = [];
    private bool _active, _busy, _connecting, _importing;
    private string? _message;
    private DateOnly _shownDate;
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);

    public ObservableCollection<CalendarAccountRow> Accounts { get; } = [];
    public ObservableCollection<CalendarEventRow> DayEvents { get; } = [];
    public ObservableCollection<CalendarEventRow> PastDayEvents { get; } = [];
    public ObservableCollection<CalendarEventRow> AgendaEvents { get; } = [];
    public bool HasPastDayEvents => PastDayEvents.Count > 0;
    public bool HasAgendaEvents => AgendaEvents.Count > 0;
    public bool IsDayEmpty => DayEvents.Count == 0 && !HasPastDayEvents && !IsBusy;
    public int DayVersion { get; private set; }
    public string DayHint => Strings.Get(!HasAccounts ? "Calendar_DayConnectHint" :
        !_service.Accounts.Any(a => SelectedDate >= DateOnly.FromDateTime(a.WindowStart.LocalDateTime) &&
            SelectedDate < DateOnly.FromDateTime(a.WindowEnd.LocalDateTime)) ? "Calendar_DayOutsideRange" : "Calendar_DayEmpty");
    public DateOnly SelectedDate
    {
        get => _selectedDate;
        set { if (_selectedDate == value) return; _selectedDate = value; RefreshEvents(force: true); }
    }
    public int GetEventCount(DateTimeOffset date) => _service.Accounts.SelectMany(a => a.Events)
        .Count(item => CalendarAgenda.IncludesDay(item, DateOnly.FromDateTime(date.Date), TimeZoneInfo.Local));
    public RelayCommand ConnectCommand { get; }
    public RelayCommand GoogleConnectCommand { get; }
    public RelayCommand CancelConnectCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ImportCommand { get; }
    public bool CanImport => _pickImportFile != null && !IsBusy;
    public bool HasOnlineAccounts => _service.Accounts.Any(a => a.Provider != CalendarProvider.Ics);
    public bool IsBusy => _busy || _service.IsRefreshing;
    public bool IsConnecting => _connecting && _service.IsAuthorizing;
    public bool HasAccounts => Accounts.Count > 0;
    public bool IsMicrosoftUnavailable => !_service.IsMicrosoftConfigured;
    public bool IsGoogleUnavailable => !_service.IsGoogleConfigured;
    public bool HasStatus => IsBusy || HasOnlineAccounts;
    public string StatusLabel => IsBusy ? Strings.Get(_importing ? "Calendar_Importing" : IsConnecting ? "Calendar_SigningIn" :
        _connecting ? "Calendar_Connecting" : "Calendar_Refreshing") :
        _service.Accounts.Any(a => a.Provider != CalendarProvider.Ics && a.LastSyncedAt == null) ? Strings.Get("Calendar_NotRefreshed") :
        HasOnlineAccounts ? string.Format(Strings.Get("Calendar_AgendaRefreshed"),
            _service.Accounts.Where(a => a.Provider != CalendarProvider.Ics).Min(a => a.LastSyncedAt)!.Value.ToLocalTime().ToString("g")) : string.Empty;
    public string Message => _message ?? _service.Error ?? _service.Accounts.Select(a => _service.GetAccountError(a.Id))
        .FirstOrDefault(error => error != null) ?? _service.ReminderError ?? string.Empty;
    public bool HasMessage => Message.Length > 0;
    public InfoBarSeverity MessageSeverity => InfoBarSeverity.Warning;
    public string RangeLabel => string.Format(Strings.Get("Calendar_Range"), TimeZoneInfo.Local.DisplayName);
    public event PropertyChangedEventHandler? PropertyChanged;

    public CalendarViewModel(CalendarService service, Func<Task<string?>>? pickImportFile = null)
    {
        _service = service;
        _pickImportFile = pickImportFile;
        ImportCommand = new(async _ => await PickAndImportAsync(null), _ => CanImport);
        ConnectCommand = new(async _ =>
        {
            _connecting = true;
            await RunAsync(() => _service.ConnectAsync());
        }, _ => !IsBusy && _service.IsMicrosoftConfigured && !AppDataPath.IsUiTest);
        GoogleConnectCommand = new(async _ =>
        {
            _connecting = true;
            await RunAsync(() => _service.ConnectGoogleAsync());
        }, _ => !IsBusy && _service.IsGoogleConfigured && !AppDataPath.IsUiTest);
        CancelConnectCommand = new(_ => _service.CancelConnect());
        RefreshCommand = new(async _ => await RunAsync(() => _service.RefreshAsync(force: true)),
            _ => !IsBusy && _service.IsConfigured && HasOnlineAccounts && !AppDataPath.IsUiTest);
        _clock = _dispatcher.CreateTimer();
        _clock.Interval = TimeSpan.FromMinutes(1);
        _clock.Tick += (_, _) => RefreshEvents();
    }

    public async Task StartAsync()
    {
        if (_active) return;
        _active = true;
        _service.StateChanged += OnServiceChanged;
        _clock.Start();
        try { await _service.InitializeAsync(); Rebuild(); await _service.RefreshAsync(); }
        catch (Exception ex) { _message = CalendarService.FriendlyError(ex); Rebuild(); }
    }

    public void Stop()
    {
        if (!_active) return;
        _active = false;
        _clock.Stop();
        _service.StateChanged -= OnServiceChanged;
        if (_connecting) _service.CancelConnect();
        DayEvents.Clear();
        PastDayEvents.Clear();
        AgendaEvents.Clear();
        _shownEvents = [];
        DayVersion++;
        foreach (var name in new[] { nameof(IsDayEmpty), nameof(HasPastDayEvents), nameof(HasAgendaEvents), nameof(DayVersion) })
            PropertyChanged?.Invoke(this, new(name));
    }

    public void RefreshTheme() => RefreshEvents(force: true);

    private async Task PickAndImportAsync(string? replaceId)
    {
        if (_pickImportFile == null) return;
        _importing = true;
        try
        {
            await RunAsync(async () =>
            {
                if (await _pickImportFile() is { } path) await _service.ImportAsync(path, replaceId);
            });
        }
        finally { _importing = false; }
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        _message = null;
        Rebuild();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _message = CalendarService.FriendlyError(ex); }
        finally { _busy = false; _connecting = false; Rebuild(); }
    }

    private void OnServiceChanged() => _dispatcher.TryEnqueue(() => { if (_active) Rebuild(); });

    private void Rebuild()
    {
        Accounts.Clear();
        foreach (var account in _service.Accounts)
        {
            var imported = account.Provider == CalendarProvider.Ics;
            var status = imported ? string.Format(Strings.Get("Calendar_ImportStatus"), account.LastSyncedAt?.ToLocalTime().ToString("g"),
                account.WindowEnd.ToLocalTime().AddDays(-1).ToString("d")) : _service.GetAccountError(account.Id) ?? (account.LastSyncedAt is { } time
                ? string.Format(Strings.Get(account.Calendars.Count == 1 ? "Calendar_LastRefreshedSingle" : "Calendar_LastRefreshed"),
                    account.Calendars.Count, time.ToLocalTime().ToString("g"))
                : Strings.Get("Calendar_NotRefreshed"));
            Accounts.Add(new(account.Id, account.Email, ProviderName(account.Provider), status, new RelayCommand(
                async _ => await RunAsync(() => _service.DisconnectAsync(account.Id)), _ => !IsBusy), imported,
                new RelayCommand(async _ => await PickAndImportAsync(account.Id), _ => !IsBusy)));
        }
        RefreshEvents(force: true);
        foreach (var name in new[] { nameof(IsBusy), nameof(CanImport), nameof(IsConnecting), nameof(HasAccounts), nameof(HasOnlineAccounts),
                     nameof(StatusLabel), nameof(HasStatus),
                     nameof(Message), nameof(HasMessage), nameof(MessageSeverity), nameof(RangeLabel), nameof(IsDayEmpty), nameof(DayHint) })
            PropertyChanged?.Invoke(this, new(name));
        ConnectCommand.RaiseCanExecuteChanged();
        GoogleConnectCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        ImportCommand.RaiseCanExecuteChanged();
    }

    private void RefreshEvents(bool force = false)
    {
        var now = DateTimeOffset.Now;
        var events = _service.Accounts.SelectMany(a => a.Events).OrderBy(e => e.AccountId).ThenBy(e => e.CalendarId)
            .ThenBy(e => e.Id).ToList();
        var day = DateOnly.FromDateTime(now.LocalDateTime);
        var agendaEventCount = events.Count(item => !CalendarAgenda.HasEnded(item, now));
        if (!force && _shownDate == day && _shownEvents.SequenceEqual(events) &&
            DayEvents.Concat(PastDayEvents).All(row => row.IsPast == CalendarEventRow.HasEnded(row.Event, now)) &&
            AgendaEvents.Count == agendaEventCount) return;
        _shownDate = day;
        _shownEvents = events;
        var quickJoin = CalendarAgenda.QuickJoin(events, day, now);
        var rows = events.Select(e =>
        {
            var account = _service.Accounts.First(a => a.Id == e.AccountId);
            return new CalendarEventRow(e, ProviderName(account.Provider) + " · " + account.Email, now, ReferenceEquals(e, quickJoin));
        }).ToArray();
        var selectedDate = SelectedDate;
        var selectedDayRows = rows.Where(row => CalendarAgenda.IncludesDay(row.Event, selectedDate, TimeZoneInfo.Local)).ToArray();
        var upcoming = selectedDayRows.Where(r => !r.IsPast).OrderBy(r => r.SortStart).ToList();
        var past = selectedDayRows.Where(r => r.IsPast).OrderByDescending(r => r.Event.End).ToList();
        DayEvents.Clear();
        PastDayEvents.Clear();
        AgendaEvents.Clear();
        foreach (var row in upcoming)
            DayEvents.Add(row);
        foreach (var row in past)
            PastDayEvents.Add(row);
        foreach (var row in rows.Where(row => !row.IsPast).OrderBy(row => row.SortStart))
            AgendaEvents.Add(row);
        DayVersion++;
        foreach (var name in new[] { nameof(IsDayEmpty), nameof(HasPastDayEvents), nameof(HasAgendaEvents), nameof(DayHint), nameof(DayVersion) })
            PropertyChanged?.Invoke(this, new(name));
    }

    private static string ProviderName(CalendarProvider provider) => Strings.Get(provider switch
    {
        CalendarProvider.Google => "Calendar_ProviderGoogle",
        CalendarProvider.Ics => "Calendar_ProviderIcs",
        _ => "Calendar_ProviderMicrosoft"
    });
}
