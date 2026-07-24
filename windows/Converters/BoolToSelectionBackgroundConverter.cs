using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Converters;

public sealed class BoolToSelectionBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool selected = value is true;
        return ThemeResourceHelper.GetBrush(selected
            ? "SubtleFillColorSecondaryBrush"
            : "CardBackgroundFillColorDefaultBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
