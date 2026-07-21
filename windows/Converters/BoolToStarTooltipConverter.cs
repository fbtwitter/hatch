using Hatch.Helpers;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class BoolToStarTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Strings.Task_Tooltip_Star_Remove : Strings.Task_Tooltip_Star_Add;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
