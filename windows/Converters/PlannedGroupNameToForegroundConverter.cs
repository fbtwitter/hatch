using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class PlannedGroupNameToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is string name && name == "Overdue")
            return ThemeResourceHelper.GetBrush("SystemFillColorCriticalBrush");

        // Returning null marshals to a nullptr IBrush* in native XAML — the renderer
        // dereferences it to paint the TextBlock and crashes with AV at 0x0.
        // UnsetValue tells the binding engine to skip setting the property entirely,
        // leaving the TextBlock with its inherited theme foreground.
        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
