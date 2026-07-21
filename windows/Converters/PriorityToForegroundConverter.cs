using Hatch.Models;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Converters;

// Chip icon/text color — saturated. Pairs with PriorityToBrushConverter for the light-tint background.
public sealed class PriorityToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush _highFgLight   = new(Windows.UI.Color.FromArgb(255, 196,  43,  28));
    private static readonly SolidColorBrush _highFgDark    = new(Windows.UI.Color.FromArgb(255, 231, 116, 113));
    private static readonly SolidColorBrush _mediumFgLight = new(Windows.UI.Color.FromArgb(255, 202,  80,  16));
    private static readonly SolidColorBrush _mediumFgDark  = new(Windows.UI.Color.FromArgb(255, 255, 178,  85));
    private static readonly SolidColorBrush _lowFgLight    = new(Windows.UI.Color.FromArgb(255,   0, 120, 212));
    private static readonly SolidColorBrush _lowFgDark     = new(Windows.UI.Color.FromArgb(255, 108, 180, 255));

    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        bool dark = ThemeResourceHelper.IsDarkTheme();
        var priority = value as TaskPriority? ?? TaskPriority.None;

        return priority switch
        {
            TaskPriority.High   => dark ? _highFgDark   : _highFgLight,
            TaskPriority.Medium => dark ? _mediumFgDark : _mediumFgLight,
            _                   => dark ? _lowFgDark    : _lowFgLight,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
