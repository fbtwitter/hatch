using Hatch.Models;

namespace Hatch.ViewModels;

[WinRT.GeneratedBindableCustomProperty]
public sealed partial class PlannedAgendaEntry
{
    private PlannedAgendaEntry(TodoItem? task, CalendarEventRow? meeting, string? sectionTitle, string? dateTitle)
        => (Task, Meeting, SectionTitle, DateTitle) = (task, meeting, sectionTitle, dateTitle);

    public TodoItem? Task { get; }
    public CalendarEventRow? Meeting { get; }
    public string? SectionTitle { get; }
    public string? DateTitle { get; }
    public bool HasTask => Task != null;
    public bool HasMeeting => Meeting != null;
    public bool HasSectionTitle => SectionTitle != null;
    public bool HasDateTitle => DateTitle != null;

    public static PlannedAgendaEntry ForTask(TodoItem task) => new(task, null, null, null);
    public static PlannedAgendaEntry ForMeeting(CalendarEventRow meeting) => new(null, meeting, null, null);
    public static PlannedAgendaEntry ForSection(string title) => new(null, null, title, null);
    public static PlannedAgendaEntry ForDate(string title) => new(null, null, null, title);
}
