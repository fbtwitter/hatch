using System.Text.Json;
using Hatch.Models;

namespace Hatch.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    // SaveAsync is called fire-and-forget from many settings setters — serialize the
    // writes so they can't interleave on the same file.
    private static readonly SemaphoreSlim _fileLock = new(1, 1);

    private readonly string _filePath;
    private CancellationTokenSource? _debounceToken;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Hatch");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "settings.json");
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch { Current = new(); }
    }

    // Debounced: coalesces rapid successive settings changes (drag, resize, toggles,
    // tip bookkeeping) into a single write after 500ms idle, matching tasks.json's
    // save pattern. Callers that must observe the write completing (onboarding,
    // sign-out, token persistence) should await SaveAsync() directly instead.
    public void SaveDebounced()
    {
        var previous = _debounceToken;
        previous?.Cancel();
        _debounceToken = new CancellationTokenSource();
        _ = DebouncedSaveAsync(_debounceToken.Token);
        previous?.Dispose();
    }

    private async Task DebouncedSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            await SaveAsync();
        }
        catch (OperationCanceledException) { }
    }

    // Exit path only. Application.Current.Exit() tears the process down without draining
    // the dispatcher, so a debounced write still inside its 500ms window would simply be
    // lost — a setting changed moments before quitting would not survive the restart.
    // Writing synchronously is deliberate here: there is no later frame for an async
    // continuation to run on. Uses its own temp name so it cannot collide with an
    // in-flight async write's temp file.
    public void FlushPendingSave()
    {
        if (_debounceToken is null || _debounceToken.IsCancellationRequested) return;
        _debounceToken.Cancel();
        try
        {
            var json = JsonSerializer.Serialize(Current, _options);
            var tmpPath = _filePath + ".exit.tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch { }
    }

    public async Task SaveAsync()
    {
        // An explicit immediate save supersedes any pending debounced one.
        _debounceToken?.Cancel();

        var json = JsonSerializer.Serialize(Current, _options);
        await _fileLock.WaitAsync();
        try
        {
            var tmpPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
