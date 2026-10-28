using Hatch.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hatch.Helpers;

public sealed partial class PlannedAgendaItemStyleSelector : StyleSelector
{
    public Style TaskStyle { get; set; } = null!;
    public Style DefaultStyle { get; set; } = null!;

    protected override Style SelectStyleCore(object item, DependencyObject container) =>
        item is PlannedAgendaEntry { HasTask: true } ? TaskStyle : DefaultStyle;
}
