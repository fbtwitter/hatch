using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Converters;

public sealed class DueDateToChipForegroundConverter : IValueConverter
{
    // Cached per light/dark variant to avoid allocating a new SolidColorBrush on every binding evaluation.
    private static readonly SolidColorBrush _overdueFgLight  = new(Windows.UI.Color.FromArgb(255, 162,  32,  32));
    private static readonly SolidColorBrush _overdueFgDark   = new(Windows.UI.Color.FromArgb(255, 255, 120, 110));
    private static readonly SolidColorBrush _todayFgLight    = new(Windows.UI.Color.FromArgb(255,   0,  90, 200));
    private static readonly SolidColorBrush _todayFgDark     = new(Windows.UI.Color.FromArgb(255, 130, 190, 255));
    private static readonly SolidColorBrush _upcomingFgLight = new(Windows.UI.Color.FromArgb(255,  30,  30,  30));
    private static readonly SolidColorBrush _upcomingFgDark  = new(Windows.UI.Color.FromArgb(255, 220, 220, 220));
    private static readonly SolidColorBrush _noneFgLight     = new(Windows.UI.Color.FromArgb(255,  80,  80,  80));
    private static readonly SolidColorBrush _noneFgDark      = new(Windows.UI.Color.FromArgb(255, 180, 180, 180));

    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        bool dark = ThemeResourceHelper.IsDarkTheme();

        if (value is not DateTimeOffset dto)
            return dark ? _noneFgDark : _noneFgLight;

        var diff = (DateTimeOffset.Now.Date - dto.Date).Days;

        return diff switch
        {
            > 0 => dark ? _overdueFgDark  : _overdueFgLight,
            0   => dark ? _todayFgDark    : _todayFgLight,
            _   => dark ? _upcomingFgDark : _upcomingFgLight,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
