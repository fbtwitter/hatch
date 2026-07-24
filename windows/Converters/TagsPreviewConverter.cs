using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class TagsPreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is List<string> tags)
            return tags.Count <= 2 ? tags : tags.Take(2).ToList();
        return Array.Empty<string>();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
