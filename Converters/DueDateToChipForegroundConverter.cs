using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class DueDateToChipForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is not DateTimeOffset dto)
            return ThemeResourceHelper.GetThemedBrush(
                Windows.UI.Color.FromArgb(255,  80,  80,  80),   // light: dark grey
                Windows.UI.Color.FromArgb(255, 180, 180, 180));  // dark:  light grey

        var diff = (DateTimeOffset.Now.Date - dto.ToLocalTime().Date).Days;

        // diff > 0 = overdue | diff = 0 = today | diff < 0 = upcoming
        return diff switch
        {
            > 0 => ThemeResourceHelper.GetThemedBrush(
                       Windows.UI.Color.FromArgb(255, 162,  32,  32),   // light: muted dark red
                       Windows.UI.Color.FromArgb(255, 255, 120, 110)),  // dark:  soft red

            0   => ThemeResourceHelper.GetThemedBrush(
                       Windows.UI.Color.FromArgb(255,   0,  90, 200),   // light: blue
                       Windows.UI.Color.FromArgb(255, 130, 190, 255)),  // dark:  soft blue

            _   => ThemeResourceHelper.GetThemedBrush(
                       Windows.UI.Color.FromArgb(255,  30,  30,  30),   // light: near-black
                       Windows.UI.Color.FromArgb(255, 220, 220, 220))   // dark:  near-white
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}