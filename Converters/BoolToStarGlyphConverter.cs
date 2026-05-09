using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class BoolToStarGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? "\xE735" : "\xE734";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
