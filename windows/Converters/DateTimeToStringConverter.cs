using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is DateTime dt ? dt.ToString("MMM d, yyyy  h:mm tt") : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
