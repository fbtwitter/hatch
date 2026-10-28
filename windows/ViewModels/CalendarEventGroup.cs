namespace Hatch.ViewModels;

[WinRT.GeneratedBindableCustomProperty]
public sealed partial class CalendarEventGroup(string name, List<CalendarEventRow> items)
{
    public string Name => name;
    public List<CalendarEventRow> Items => items;
}
