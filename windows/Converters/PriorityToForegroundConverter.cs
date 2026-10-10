using Hatch.Models;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class PriorityToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
        => ThemeResourceHelper.GetBrush(value switch
        {
            TaskPriority.High => "SystemFillColorCriticalBrush",
            TaskPriority.Medium => "SystemFillColorCautionBrush",
            _ => "AccentTextFillColorPrimaryBrush"
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
