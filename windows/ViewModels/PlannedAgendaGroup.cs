namespace Hatch.ViewModels;

public sealed record PlannedAgendaGroup(string Name, IReadOnlyList<PlannedAgendaEntry> Items);
