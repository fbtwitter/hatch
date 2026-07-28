using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class DueDateToChipGlyphConverter : IValueConverter
{
    private const string CalendarGlyph = "\uE787";  // Calendar
    private const string WarningGlyph  = "\uE7BA";  // Warning

    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is not DateTimeOffset dto)
            return CalendarGlyph;

        var diff = (DateTimeOffset.Now.Date - dto.Date).Days;
        return diff >= 7 ? WarningGlyph : CalendarGlyph;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
