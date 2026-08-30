using Microsoft.UI.Xaml.Media;

namespace Hatch.ViewModels;

// One bar in the Summary page's "This week" completion strip. BarHeight is pre-resolved to
// device-independent pixels by StatsViewModel (it owns the band dimensions) so the view
// needs no converter. DayLabel is the narrow weekday initial.
public sealed record RhythmBarInfo(
    double BarHeight,
    Brush Fill,
    string DayLabel,
    Brush LabelForeground,
    bool IsToday);
