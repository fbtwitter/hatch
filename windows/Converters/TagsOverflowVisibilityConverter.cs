using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class TagsOverflowVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is List<string> { Count: > 2 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
