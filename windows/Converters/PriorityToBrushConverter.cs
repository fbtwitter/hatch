using Hatch.Models;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class PriorityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
        => ThemeResourceHelper.GetBrush(value switch
        {
            TaskPriority.High => "SystemFillColorCriticalBackgroundBrush",
            TaskPriority.Medium => "SystemFillColorCautionBackgroundBrush",
            _ => "SystemFillColorAttentionBackgroundBrush"
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
