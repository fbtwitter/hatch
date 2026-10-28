namespace Hatch.ViewModels;

public sealed partial record CalendarAccountRow(string Id, string Email, string ProviderName, string Status,
    RelayCommand DisconnectCommand, bool IsImported, RelayCommand ReplaceCommand)
{
    public string DisplayName => ProviderName + " · " + Email;
    public string RemoveLabel => Hatch.Helpers.Strings.Get(IsImported ? "Calendar_RemoveImport" : "Calendar_DisconnectLabel");
    public string DisconnectName => string.Format(Hatch.Helpers.Strings.Get(IsImported ? "Calendar_RemoveImportName" : "Calendar_DisconnectName"), DisplayName);
    public string DisconnectAutomationId => "Calendar_Disconnect_" + Id;
    public string OptionsName => string.Format(Hatch.Helpers.Strings.Get("Calendar_AccountOptionsName"), DisplayName);
    public string OptionsAutomationId => "Calendar_AccountOptions_" + Id;
    public string ReplaceAutomationId => "Calendar_Replace_" + Id;
}
