using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Converters;

public sealed class DueDateToChipBrushConverter : IValueConverter
{
    // Cached per light/dark variant to avoid allocating a new SolidColorBrush on every binding evaluation.
    private static readonly SolidColorBrush _overdueBgLight  = new(Windows.UI.Color.FromArgb(255, 252, 226, 224));
    private static readonly SolidColorBrush _overdueBgDark   = new(Windows.UI.Color.FromArgb(255,  80,  25,  25));
    private static readonly SolidColorBrush _todayBgLight    = new(Windows.UI.Color.FromArgb(255, 219, 235, 255));
    private static readonly SolidColorBrush _todayBgDark     = new(Windows.UI.Color.FromArgb(255,  20,  50,  90));
    private static readonly SolidColorBrush _upcomingBgLight = new(Windows.UI.Color.FromArgb(255, 225, 225, 225));
    private static readonly SolidColorBrush _upcomingBgDark  = new(Windows.UI.Color.FromArgb(255,  55,  55,  55));

    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        bool dark = ThemeResourceHelper.IsDarkTheme();

        if (value is not DateTimeOffset dto)
            return dark ? _upcomingBgDark : _upcomingBgLight;

        var diff = (DateTimeOffset.Now.Date - dto.Date).Days;

        return diff switch
        {
            > 0 => dark ? _overdueBgDark  : _overdueBgLight,
            0   => dark ? _todayBgDark    : _todayBgLight,
            _   => dark ? _upcomingBgDark : _upcomingBgLight,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
