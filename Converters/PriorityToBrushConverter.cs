using Hatch.Models;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Converters;

// Chip background — light tint. Pairs with PriorityToForegroundConverter for the saturated icon/text color.
public sealed class PriorityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush _highBgLight   = new(Windows.UI.Color.FromArgb(255, 252, 226, 224));
    private static readonly SolidColorBrush _highBgDark    = new(Windows.UI.Color.FromArgb(255,  75,  30,  28));
    private static readonly SolidColorBrush _mediumBgLight = new(Windows.UI.Color.FromArgb(255, 255, 235, 217));
    private static readonly SolidColorBrush _mediumBgDark  = new(Windows.UI.Color.FromArgb(255,  75,  48,  20));
    private static readonly SolidColorBrush _lowBgLight    = new(Windows.UI.Color.FromArgb(255, 219, 235, 255));
    private static readonly SolidColorBrush _lowBgDark     = new(Windows.UI.Color.FromArgb(255,  20,  50,  90));

    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        bool dark = ThemeResourceHelper.IsDarkTheme();
        var priority = value as TaskPriority? ?? TaskPriority.None;

        return priority switch
        {
            TaskPriority.High   => dark ? _highBgDark   : _highBgLight,
            TaskPriority.Medium => dark ? _mediumBgDark : _mediumBgLight,
            _                   => dark ? _lowBgDark    : _lowBgLight,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
