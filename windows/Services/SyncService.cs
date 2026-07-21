using System.Text.Json;
using Hatch.Helpers;
using Hatch.Models;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using SupabaseClient = Supabase.Client;
using GotrueConstants = Supabase.Gotrue.Constants;
using GotrueSignInOptions = Supabase.Gotrue.SignInOptions;

namespace Hatch.Services;

// Supabase row model — maps to the user_data table
[Table("user_data")]
internal sealed class UserDataRow : BaseModel
{
    [PrimaryKey("user_id", false)]
    public string UserId { get; set; } = "";

    [Column("tasks_json")]
    public string TasksJson { get; set; } = "";

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public sealed class SyncService
{
    private const string SupabaseUrl = Secrets.SupabaseUrl;
    private const string SupabaseKey = Secrets.SupabaseKey;

    private SupabaseClient? _client;
    private PeriodicTimer? _autoSyncTimer;
    private CancellationTokenSource? _autoSyncCts;
    private CancellationTokenSource? _pushDebounce;

    public bool IsSignedIn => _client?.Auth?.CurrentSession != null;
    public string? UserEmail => _client?.Auth?.CurrentUser?.Email;
    public bool HasPassphrase => SyncPassphraseStore.Load().Passphrase != null;

    public void SetPassphrase(string passphrase)
    {
        SyncPassphraseStore.Save(passphrase);
        StateChanged?.Invoke();
    }

    public event Action? StateChanged;
    public event Action? TasksReceived; // fires when a pull returned newer data

    public void StartAutoSync()
    {
        StopAutoSync();
        if (!IsSignedIn || !HasPassphrase) return;
        _autoSyncCts = new CancellationTokenSource();
        _autoSyncTimer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        _ = RunAutoSyncLoopAsync(_autoSyncCts.Token);
    }

    public void StopAutoSync()
    {
        _autoSyncCts?.Cancel();
        _autoSyncTimer?.Dispose();
        _autoSyncTimer = null;
        _autoSyncCts = null;
    }

    private async Task RunAutoSyncLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _autoSyncTimer!.WaitForNextTickAsync(ct))
                await PullIfNewerAsync();
        }
        catch (OperationCanceledException) { }
    }

    public void SchedulePush(TasksFile data)
    {
        if (!IsSignedIn) return;
        _pushDebounce?.Cancel();
        _pushDebounce = new CancellationTokenSource();
        _ = PushAfterDelayAsync(data, _pushDebounce.Token);
    }

    private async Task PushAfterDelayAsync(TasksFile data, CancellationToken ct)
    {
        try
        {
            await Task.Delay(3000, ct);
            await PushAsync(data);
        }
        catch (OperationCanceledException) { }
    }

    public async Task InitializeAsync()
    {
        var options = new Supabase.SupabaseOptions { AutoRefreshToken = true };
        _client = new SupabaseClient(SupabaseUrl, SupabaseKey, options);
        await _client.InitializeAsync();
        await RestoreSessionAsync();
    }

    private async Task RestoreSessionAsync()
    {
        var (access, refresh) = SyncTokenStore.Load();
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return;
        try
        {
            await _client!.Auth.SetSession(access, refresh);
            StateChanged?.Invoke();
        }
        catch
        {
            ClearTokens();
            _ = App.SettingsService.SaveAsync();
        }
    }

    // Returns null on success, error message on failure.
    public async Task<string?> SignInAsync(string email, string password)
    {
        if (_client == null) return Strings.Sync_Error_NotReady;
        try
        {
            var session = await _client.Auth.SignIn(email, password);
            if (session?.AccessToken == null) return Strings.Sync_Error_SignInFailed;
            await PersistSessionAsync(session);
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Returns null on success, message on partial success (email confirmation needed).
    public async Task<string?> SignUpAsync(string email, string password)
    {
        if (_client == null) return Strings.Sync_Error_NotReady;
        try
        {
            var session = await _client.Auth.SignUp(email, password);
            if (session?.AccessToken != null)
            {
                await PersistSessionAsync(session);
                StateChanged?.Invoke();
                return null;
            }
            // Email confirmation required
            return Strings.Sync_Info_ConfirmEmail;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task SignOutAsync()
    {
        try { await (_client?.Auth?.SignOut() ?? Task.CompletedTask); } catch { }
        StopAutoSync();
        SyncPassphraseStore.Clear();
        ClearTokens();
        await App.SettingsService.SaveAsync();
        StateChanged?.Invoke();
    }

    // Returns null on success, error message on failure.
    // mergeFirst=false is only for the conflict-resolution paths, where the user has
    // explicitly chosen to replace the server copy.
    public async Task<string?> PushAsync(TasksFile data, bool mergeFirst = true)
    {
        if (_client == null) return Strings.Sync_Error_NotReady;
        if (!IsSignedIn)    return Strings.Sync_Error_NotSignedIn;
        var userId = _client.Auth.CurrentUser?.Id;
        if (string.IsNullOrEmpty(userId)) return Strings.Sync_Error_NoUserId;
        var (passphrase, salt) = SyncPassphraseStore.Load();
        if (passphrase == null || salt == null) return Strings.Sync_Error_NoPassphrase;
        try
        {
            if (mergeFirst)
            {
                var (merged, mergeError) = await MergeWithServerAsync(data);
                if (mergeError != null) return mergeError;
                if (merged != null)
                {
                    data = merged;
                    await new TaskStorageService().SaveAsync(data);
                    TasksReceived?.Invoke();
                }
            }

            var json = SyncWire.Serialize(data);
            await _client.From<UserDataRow>().Upsert(new UserDataRow
            {
                UserId    = userId,
                TasksJson = SyncCrypto.Encrypt(json, passphrase, salt),
                UpdatedAt = DateTime.UtcNow
            });
            App.Settings.LastSyncedAt = DateTime.UtcNow;
            _ = App.SettingsService.SaveAsync();
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // The row is a whole-state upsert, not a delta, so a client holding stale state would
    // otherwise replace server changes it has never seen — silent data loss the moment a
    // second device exists. Returns the union when the server is ahead of our last sync,
    // (null, null) when there is nothing to merge, and an error when the row exists but
    // cannot be read (unreadable row rule: never overwrite it).
    private async Task<(TasksFile? Merged, string? Error)> MergeWithServerAsync(TasksFile local)
    {
        var response = await _client!.From<UserDataRow>().Get();
        var row = response.Models.FirstOrDefault();
        if (string.IsNullOrEmpty(row?.TasksJson)) return (null, null);

        if (App.Settings.LastSyncedAt.HasValue &&
            row.UpdatedAt <= App.Settings.LastSyncedAt.Value)
            return (null, null);

        var (server, readError) = ReadServerTasks(row);
        if (readError != null) return (null, readError);
        if (server == null) return (null, null);

        return (SyncMerge.Merge(local, server), null);
    }

    // Returns the GitHub OAuth URL to open in the browser, or null on failure.
    public async Task<string?> GetGitHubSignInUrlAsync()
    {
        if (_client == null) return null;
        try
        {
            var state = await _client.Auth.SignIn(
                GotrueConstants.Provider.Github,
                new GotrueSignInOptions
                {
                    RedirectTo = "hatch://auth-callback",
                    FlowType   = GotrueConstants.OAuthFlowType.Implicit
                });
            return state?.Uri?.ToString();
        }
        catch { return null; }
    }

    // Called when the app is activated via hatch://auth-callback after GitHub OAuth.
    public async Task HandleOAuthCallbackAsync(Uri callbackUri)
    {
        if (_client == null) return;
        try
        {
            // Implicit flow puts tokens in the URL fragment: #access_token=...&refresh_token=...
            var fragment = callbackUri.Fragment.TrimStart('#');
            var p = ParseQueryString(fragment);

            var access  = p.GetValueOrDefault("access_token");
            var refresh = p.GetValueOrDefault("refresh_token");
            if (string.IsNullOrEmpty(access)) return;

            await _client.Auth.SetSession(access, refresh ?? "");
            var session = _client.Auth.CurrentSession;
            if (session != null) await PersistSessionAsync(session);
            StateChanged?.Invoke();
        }
        catch { }
    }

    // Single reader for server payloads. Error is set when a row exists but cannot be
    // read — callers must treat that as "server has data" and never overwrite it.
    // Plaintext rows predate E2E encryption; they parse as-is and get encrypted on the
    // next push.
    private static (TasksFile? Data, string? Error) ReadServerTasks(UserDataRow? row)
    {
        if (string.IsNullOrEmpty(row?.TasksJson)) return (null, null);

        string json = row.TasksJson;
        if (SyncCrypto.IsEncrypted(json))
        {
            var (passphrase, _) = SyncPassphraseStore.Load();
            if (passphrase == null) return (null, Strings.Sync_Error_NoPassphrase);
            var plain = SyncCrypto.TryDecrypt(json, passphrase);
            if (plain == null) return (null, Strings.Sync_Error_WrongPassphrase);
            json = plain;
        }

        try   { return (SyncWire.Deserialize(json), null); }
        catch { return (null, Strings.Sync_Error_WrongPassphrase); }
    }

    private static Dictionary<string, string> ParseQueryString(string query)
        => query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(
                    p => Uri.UnescapeDataString(p[0]),
                    p => Uri.UnescapeDataString(p[1]));

    // Returns null on success/no-op, error message on failure.
    // force=true (user-triggered sync) bypasses the staleness check and always downloads.
    public async Task<string?> PullIfNewerAsync(bool force = false)
    {
        if (!IsSignedIn || _client == null) return null;
        try
        {
            var response = await _client.From<UserDataRow>().Get();
            var row = response.Models.FirstOrDefault();
            if (row?.TasksJson == null) return null;

            if (!force &&
                App.Settings.LastSyncedAt.HasValue &&
                row.UpdatedAt <= App.Settings.LastSyncedAt.Value)
                return null;

            var (data, readError) = ReadServerTasks(row);
            if (readError != null) return readError;
            if (data == null) return null;

            await new TaskStorageService().SaveAsync(data);
            App.Settings.LastSyncedAt = row.UpdatedAt;
            _ = App.SettingsService.SaveAsync();
            TasksReceived?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Checks whether both local and server have tasks after a fresh sign-in.
    // Returns SyncConflict when both sides have data and the user must choose.
    // Returns null when there is no conflict; also pushes local data to the server
    // when the account is new/empty so existing tasks are backed up immediately.
    public async Task<SyncConflict?> CheckConflictAsync()
    {
        if (!IsSignedIn || _client == null) return null;
        try
        {
            var localData = await new TaskStorageService().LoadAsync();
            var response  = await _client.From<UserDataRow>().Get();
            var row       = response.Models.FirstOrDefault();

            var (serverData, readError) = ReadServerTasks(row);
            // Unreadable server data (missing/wrong passphrase) must never be treated as
            // an empty account — pushing here would overwrite it.
            if (readError != null) return null;

            bool localHasData  = localData.Tasks.Count > 0;
            bool serverHasData = (serverData?.Tasks.Count ?? 0) > 0;

            if (localHasData && serverHasData)
            {
                var localPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Hatch", "tasks.json");
                var localLastMod = File.Exists(localPath)
                    ? File.GetLastWriteTimeUtc(localPath)
                    : DateTime.MinValue;

                return new SyncConflict(
                    localData.Tasks.Count,
                    localData.Lists.Count,
                    localLastMod,
                    serverData!.Tasks.Count,
                    serverData.Lists.Count,
                    row!.UpdatedAt);
            }

            // Server is empty but local has tasks → push local so it's backed up.
            if (localHasData)
                await PushAsync(localData);

            return null;
        }
        catch { return null; }
    }

    public async Task<string?> ResolveConflictUseLocalAsync()
    {
        var data = await new TaskStorageService().LoadAsync();
        // "Use local" means replace the server copy — merging here would silently do the
        // opposite of what the user picked.
        return await PushAsync(data, mergeFirst: false);
    }

    public Task<string?> ResolveConflictUseServerAsync()
        => PullIfNewerAsync(force: true);

    // Non-destructive alternative to picking a side: unions both datasets (see SyncMerge),
    // saves the result locally, and pushes it back so both sides converge.
    public async Task<string?> ResolveConflictMergeAsync()
    {
        if (!IsSignedIn || _client == null) return Strings.Sync_Error_NotSignedIn;
        try
        {
            var local = await new TaskStorageService().LoadAsync();
            var response = await _client.From<UserDataRow>().Get();
            var row = response.Models.FirstOrDefault();
            var (server, readError) = ReadServerTasks(row);
            if (readError != null) return readError;

            var merged = SyncMerge.Merge(local, server ?? new TasksFile());
            await new TaskStorageService().SaveAsync(merged);
            TasksReceived?.Invoke();
            return await PushAsync(merged, mergeFirst: false);
        }
        catch (Exception ex) { return ex.Message; }
    }

    private async Task PersistSessionAsync(Supabase.Gotrue.Session session)
    {
        SyncTokenStore.Save(session.AccessToken, session.RefreshToken);
        App.Settings.SyncUserEmail = _client?.Auth.CurrentUser?.Email;
        await App.SettingsService.SaveAsync();
    }

    private static void ClearTokens()
    {
        SyncTokenStore.Clear();
        App.Settings.SyncUserEmail = null;
        App.Settings.LastSyncedAt  = null;
    }
}
