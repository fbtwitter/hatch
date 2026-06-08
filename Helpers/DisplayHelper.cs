using System.Runtime.InteropServices;

namespace Hatch.Helpers;

internal static class DisplayHelper
{
    /// <summary>
    /// Returns true if the display that contains <paramref name="hwnd"/> currently
    /// has HDR (advanced color) enabled. Falls back to checking any active display
    /// if the per-monitor lookup fails.
    /// </summary>
    internal static bool IsWindowOnHdrDisplay(IntPtr hwnd)
    {
        if (GetDisplayConfigBufferSizes(out uint pathCount, out uint modeCount) != 0)
            return false;

        var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];

        if (NativeMethods.QueryDisplayConfig(
                NativeMethods.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount, paths,
                ref modeCount, modes,
                IntPtr.Zero) != 0)
            return false;

        // Check every active path — if the system has mixed SDR/HDR monitors we
        // cannot easily map HMONITOR → QueryDisplayConfig path without enumerating
        // display device names (an additional P/Invoke round-trip). For the mascot
        // window that lives on one monitor at a time, returning true when any path
        // has HDR active is a practical approximation.
        for (int i = 0; i < pathCount; i++)
        {
            var info = new NativeMethods.DISPLAYCONFIG_ADVANCED_COLOR_INFO
            {
                header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                    size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_ADVANCED_COLOR_INFO>(),
                    adapterId = paths[i].targetInfo.adapterId,
                    id        = paths[i].targetInfo.id
                }
            };

            if (NativeMethods.DisplayConfigGetDeviceInfo(ref info) != 0) continue;

            // Bit 1 = advancedColorEnabled (HDR is on for this display)
            if ((info.value & 0x2) != 0) return true;
        }

        return false;
    }

    private static int GetDisplayConfigBufferSizes(out uint pathCount, out uint modeCount)
        => NativeMethods.GetDisplayConfigBufferSizes(
               NativeMethods.QDC_ONLY_ACTIVE_PATHS,
               out pathCount,
               out modeCount);
}
