using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class DueDateToChipBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is not DateTimeOffset dto)
            return ThemeResourceHelper.GetThemedBrush(
                Windows.UI.Color.FromArgb(255, 225, 225, 225),   // light: light grey
                Windows.UI.Color.FromArgb(255,  55,  55,  55));  // dark:  dark grey

        var diff = (DateTimeOffset.Now.Date - dto.ToLocalTime().Date).Days;

        // diff > 0 = overdue | diff = 0 = today | diff < 0 = upcoming
        return diff switch
        {
            > 0 => ThemeResourceHelper.GetThemedBrush(
                       Windows.UI.Color.FromArgb(255, 252, 226, 224),   // light: soft rose
                       Windows.UI.Color.FromArgb(255,  80,  25,  25)),  // dark:  deep red

            0   => ThemeResourceHelper.GetThemedBrush(
                       Windows.UI.Color.FromArgb(255, 219, 235, 255),   // light: soft blue
                       Windows.UI.Color.FromArgb(255,  20,  50,  90)),  // dark:  deep blue

            _   => ThemeResourceHelper.GetThemedBrush(
                       Windows.UI.Color.FromArgb(255, 225, 225, 225),   // light: light grey
                       Windows.UI.Color.FromArgb(255,  55,  55,  55))   // dark:  dark grey
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
