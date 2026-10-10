using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class DueDateToChipBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        var key = value is DateTimeOffset date
            ? (DateTimeOffset.Now.Date - date.Date).Days switch
            {
                > 0 => "SystemFillColorCriticalBackgroundBrush",
                0 => "SystemFillColorAttentionBackgroundBrush",
                _ => "ControlFillColorSecondaryBrush"
            }
            : "ControlFillColorSecondaryBrush";
        return ThemeResourceHelper.GetBrush(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
