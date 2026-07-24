namespace Hatch.Models;

public sealed record SyncConflict(
    int LocalTaskCount,
    int LocalListCount,
    DateTime LocalLastModified,   // UTC
    int ServerTaskCount,
    int ServerListCount,
    DateTime ServerLastModified); // UTC
