using Hatch.Models;

namespace Hatch.Helpers;

// Lives in Helpers (not TipCoordinator) so the WinUI-free test project can link it.
public static class TipSchedule
{
    public static bool IsInPreferredWindow(DateTime now, TipTimePreference preference) => preference switch
    {
        TipTimePreference.Morning => now.Hour is >= 5 and < 12,
        TipTimePreference.Afternoon => now.Hour is >= 12 and < 18,
        TipTimePreference.Evening => now.Hour >= 18,
        _ => true
    };
}
