using Microsoft.UI.Xaml.Media;

namespace Hatch.ViewModels;

// Value is a pre-formatted string, not a raw number — the ViewModel formats once rather
// than the view formatting on every bind. ContainerBackground tints the whole tile by
// meaning (success / critical / gold / neutral); IconForeground matches.
public sealed record StatTileInfo(
    string AutomationId,
    string Title,
    string Value,
    string Description,
    string IconGlyph,
    Brush IconForeground,
    Brush ContainerBackground,
    string? NavTag)
{
    // A screen reader announces the button's Name and nothing inside it, so binding the
    // title alone would drop the number the tile exists to convey.
    public string AutomationName => $"{Title}: {Value} {Description}";
}
