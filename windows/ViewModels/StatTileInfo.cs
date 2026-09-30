using Microsoft.UI.Xaml.Media;

namespace Hatch.ViewModels;

// Value is a pre-formatted string, not a raw number — the ViewModel formats once rather
// than the view formatting on every bind. IconForeground carries semantic emphasis.
public sealed record StatTileInfo(
    string AutomationId,
    string Title,
    string Value,
    string Description,
    string IconGlyph,
    Brush IconForeground,
    string? NavTag)
{
    // A screen reader announces the button's Name and nothing inside it, so binding the
    // title alone would drop the number the tile exists to convey.
    public string AutomationName => $"{Title}: {Value} {Description}";
}
