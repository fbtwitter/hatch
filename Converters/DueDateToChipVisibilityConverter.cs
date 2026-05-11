using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class DueDateToChipVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        bool hasDate = value is DateTimeOffset;
        if (parameter?.ToString() == "Invert") hasDate = !hasDate;
        return hasDate ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
