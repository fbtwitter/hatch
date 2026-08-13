namespace Hatch.ViewModels;

// One entry in the "Default page" ComboBox on SettingsPage. Tag is null for "remember last
// page", otherwise one of MainWindow.NavigateTo's existing nav tags. ToString() drives the
// ComboBox's default display — no ItemTemplate needed.
public sealed record MascotOpenPageOption(string? Tag, string DisplayName)
{
    public override string ToString() => DisplayName;
}
