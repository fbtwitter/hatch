using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class TagsOverflowCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is List<string> { Count: > 2 } tags ? $"+{tags.Count - 2}" : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
