using Hatch.Helpers;
using Microsoft.UI.Xaml.Data;

namespace Hatch.Converters;

public sealed class DueDateToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is not DateTimeOffset dto)
            return string.Empty;

        var today = DateTimeOffset.Now.Date;
        var dueDate = dto.ToLocalTime().Date;
        var diff = (today - dueDate).Days;

        if (diff == 0)  return Strings.DueDate_Today;
        if (diff == -1) return Strings.DueDate_Tomorrow;
        if (diff > 0)   return Strings.DueDate_Overdue(diff);

        return dueDate.ToString("ddd, MMM d");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}
