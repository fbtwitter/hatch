using Hatch.Helpers;
using Hatch.Models;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class PriorityToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not TaskPriority priority) return string.Empty;
        var label = GetLabel(priority);
        return parameter as string == "Full" ? Strings.Task_PriorityLabel(label) : label;
    }

    public static string GetLabel(TaskPriority priority)
        => priority switch
        {
            TaskPriority.Low    => Strings.Priority_Low,
            TaskPriority.Medium => Strings.Priority_Medium,
            TaskPriority.High   => Strings.Priority_High,
            _                   => Strings.Priority_None
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
