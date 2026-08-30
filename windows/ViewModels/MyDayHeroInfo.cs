using Microsoft.UI.Xaml.Media;

namespace Hatch.ViewModels;

// The Summary page's My Day card. When something is planned it shows a determinate ring
// (RingValue is a 0-100 percentage) with CenterLabel in the middle; otherwise it falls back
// to a plain icon and "Nothing planned yet". Always navigates to the My Day list.
public sealed record MyDayHeroInfo(
    bool HasPlan,
    double RingValue,
    string CenterLabel,
    string Title,
    string Detail,
    string IconGlyph,
    Brush Background)
{
    public string AutomationName => $"{Title}: {Detail}";
}
