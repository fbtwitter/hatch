using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class DueDateToChipForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        var key = value is DateTimeOffset date
            ? (DateTimeOffset.Now.Date - date.Date).Days switch
            {
                > 0 => "SystemFillColorCriticalBrush",
                0 => "AccentTextFillColorPrimaryBrush",
                _ => "TextFillColorPrimaryBrush"
            }
            : "TextFillColorSecondaryBrush";
        return ThemeResourceHelper.GetBrush(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
