using Microsoft.UI.Xaml.Data;
using Windows.UI.Text;

namespace TodoWinUI3.Converters;

public sealed class BoolToStrikethroughConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? TextDecorations.Strikethrough : TextDecorations.None;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
