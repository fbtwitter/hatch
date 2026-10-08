using System.Runtime.InteropServices;
using Hatch.Models;

namespace Hatch.Services;

internal static class MascotSoundPlayer
{
    private const uint Async = 0x0001;
    private const uint NoDefault = 0x0002;
    private const uint Alias = 0x00010000;
    private const uint Filename = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);

    public static void Play()
    {
        var settings = App.Settings;
        if (settings.MascotSound == MascotSound.None) return;
        if (settings.MascotSound == MascotSound.BuiltIn)
        {
            PlaySound("SystemAsterisk", IntPtr.Zero, Async | NoDefault | Alias);
            return;
        }
        if (!string.IsNullOrWhiteSpace(settings.MascotCustomSoundPath) &&
            File.Exists(settings.MascotCustomSoundPath))
            PlaySound(settings.MascotCustomSoundPath, IntPtr.Zero, Async | NoDefault | Filename);
    }
}
