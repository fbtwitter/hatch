namespace Hatch.Helpers;

internal static class AppDataPath
{
    internal static bool IsUiTest { get; } = Environment.GetEnvironmentVariable("HATCH_UI_TEST") == "1";

    internal static string Folder { get; } = IsUiTest
        ? Environment.GetEnvironmentVariable("HATCH_UI_TEST_DATA_DIR")
          ?? Path.Combine(Path.GetTempPath(), "Hatch.UiTests")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hatch");
}
