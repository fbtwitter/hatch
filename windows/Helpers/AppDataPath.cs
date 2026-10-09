namespace Hatch.Helpers;

internal static class AppDataPath
{
    internal static bool IsUiTest { get; } =
        Environment.GetEnvironmentVariable("HATCH_UI_TEST") == "1" ||
        Environment.GetCommandLineArgs().Contains("--hatch-ui-test", StringComparer.Ordinal);

    internal static string Folder { get; } = IsUiTest
        ? Environment.GetEnvironmentVariable("HATCH_UI_TEST_DATA_DIR")
          ?? GetArgumentValue("--hatch-ui-test-data-dir")
          ?? Path.Combine(Path.GetTempPath(), "Hatch.UiTests")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hatch");

    internal static string? GetArgumentValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }
}
