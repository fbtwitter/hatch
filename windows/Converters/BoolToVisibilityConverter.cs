using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool show = value is true;
        if (parameter?.ToString() == "Invert") show = !show;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
