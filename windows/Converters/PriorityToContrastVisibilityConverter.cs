using Hatch.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Windows.UI.ViewManagement;

namespace Hatch.Converters;

public sealed class PriorityToContrastVisibilityConverter : IValueConverter
{
    private static readonly AccessibilitySettings Accessibility = new();

    public object Convert(object? value, Type targetType, object? parameter, string language)
        => Accessibility.HighContrast && value is TaskPriority priority && priority != TaskPriority.None
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
