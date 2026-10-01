using Hatch.Helpers;
using Hatch.Models;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Hatch.Services;

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
    private readonly SettingsService _settings;
    private readonly SyncAccountService _account;
    private readonly TaskStorageService _storage;
    private PeriodicTimer? _autoSyncTimer;
    private CancellationTokenSource? _autoSyncCts;
    private CancellationTokenSource? _pushDebounce;

    public SyncService(SettingsService settings, SyncAccountService account, TaskStorageService storage)
    {
        _settings = settings;
        _account = account;
        _storage = storage;
        _account.SigningOut += StopAutoSync;
    }

    public event Action? StateChanged;
    public event Action? TasksReceived;

    public async Task<bool> CanDecryptServerRowAsync(string passphrase)
    {
        var client = _account.Client;
        if (client == null || !_account.IsSignedIn) return true;
        try
        {
            var response = await client.From<UserDataRow>().Get();
            var json = response.Models.FirstOrDefault()?.TasksJson;
            if (string.IsNullOrEmpty(json) || !SyncCrypto.IsEncrypted(json)) return true;
            return SyncCrypto.TryDecrypt(json, passphrase) != null;
        }
        catch
        {
            // Network failure is not a wrong passphrase.
            return true;
        }
    }

    public async Task<string?> ChangePassphraseAsync(string oldPassphrase, string newPassphrase)
    {
        if (_account.Client == null) return Strings.Sync_Error_NotReady;
        if (!_account.IsSignedIn) return Strings.Sync_Error_NotSignedIn;
        if (_account.IsMfaChallengePending) return Strings.Sync_Error_MfaRequired;
        try
        {
            var response = await _account.Client.From<UserDataRow>().Get();
            var row = response.Models.FirstOrDefault();
            var (status, data) = SyncDecisions.ReadServerPayload(row?.TasksJson, oldPassphrase);
            if (status == ServerReadStatus.Unreadable) return Strings.Sync_Error_OldPassphraseWrong;

            var toPush = data ?? new TasksFile();
            SyncPassphraseStore.Save(newPassphrase);
            var pushError = await PushAsync(toPush, mergeFirst: false);
            if (pushError != null)
            {
                SyncPassphraseStore.Save(oldPassphrase);
                return pushError;
            }

            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public void StartAutoSync()
    {
        StopAutoSync();
        if (!_account.IsSignedIn || !_account.HasPassphrase || _account.IsMfaChallengePending) return;
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
        if (!_account.IsSignedIn || _account.IsMfaChallengePending) return;
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

    public async Task<string?> PushAsync(TasksFile data, bool mergeFirst = true)
    {
        var client = _account.Client;
        if (client == null) return Strings.Sync_Error_NotReady;
        if (!_account.IsSignedIn) return Strings.Sync_Error_NotSignedIn;
        if (_account.IsMfaChallengePending) return Strings.Sync_Error_MfaRequired;
        var userId = client.Auth.CurrentUser?.Id;
        if (string.IsNullOrEmpty(userId)) return Strings.Sync_Error_NoUserId;
        var (passphrase, salt) = SyncPassphraseStore.Load();
        if (passphrase == null || salt == null) return Strings.Sync_Error_NoPassphrase;
        try
        {
            // Merge first because each upsert replaces the entire server row.
            if (mergeFirst)
            {
                var (merged, mergeError) = await MergeWithServerAsync(data);
                if (mergeError != null) return mergeError;
                if (merged != null)
                {
                    if (!SyncWire.IsEquivalent(merged, data))
                    {
                        // Reloading rebuilds task collections and nav badges, so skip it for an unchanged union.
                        await _storage.SaveAsync(merged);
                        TasksReceived?.Invoke();
                    }
                    data = merged;
                }
            }

            var json = SyncWire.Serialize(data);
            await client.From<UserDataRow>().Upsert(new UserDataRow
            {
                UserId = userId,
                TasksJson = SyncCrypto.Encrypt(json, passphrase, salt),
                UpdatedAt = DateTime.UtcNow
            });
            _settings.Current.LastSyncedAt = DateTime.UtcNow;
            _settings.SaveDebounced();
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private async Task<(TasksFile? Merged, string? Error)> MergeWithServerAsync(TasksFile local)
    {
        // The row is whole-state: an unreadable newer row must block the upsert to avoid data loss.
        var metadataResponse = await _account.Client!.From<UserDataRow>().Select("updated_at").Get();
        var metadata = metadataResponse.Models.FirstOrDefault();
        if (metadata == null || !SyncDecisions.IsServerNewer(metadata.UpdatedAt, _settings.Current.LastSyncedAt))
            return (null, null);

        var response = await _account.Client.From<UserDataRow>().Get();
        var row = response.Models.FirstOrDefault();
        if (string.IsNullOrEmpty(row?.TasksJson)) return (null, null);

        if (!SyncDecisions.IsServerNewer(row.UpdatedAt, _settings.Current.LastSyncedAt))
            return (null, null);

        var (server, readError) = ReadServerTasks(row);
        if (readError != null) return (null, readError);
        if (server == null) return (null, null);

        return (SyncMerge.Merge(local, server), null);
    }

    private static (TasksFile? Data, string? Error) ReadServerTasks(UserDataRow? row)
    {
        // Plaintext rows are supported for migration; an unreadable envelope remains an error, not an empty account.
        var (passphrase, _) = SyncPassphraseStore.Load();
        var result = SyncDecisions.ReadServerPayload(row?.TasksJson, passphrase);

        return result.Status switch
        {
            ServerReadStatus.Ok => (result.Data, null),
            ServerReadStatus.Empty => (null, null),
            ServerReadStatus.NeedsPassphrase => (null, Strings.Sync_Error_NoPassphrase),
            _ => (null, Strings.Sync_Error_WrongPassphrase),
        };
    }

    public async Task<string?> PullIfNewerAsync(bool force = false)
    {
        var client = _account.Client;
        if (!_account.IsSignedIn || client == null) return null;
        if (_account.IsMfaChallengePending) return Strings.Sync_Error_MfaRequired;
        try
        {
            UserDataRow? row;
            if (force)
            {
                var response = await client.From<UserDataRow>().Get();
                row = response.Models.FirstOrDefault();
            }
            else
            {
                var metadataResponse = await client.From<UserDataRow>().Select("updated_at").Get();
                var metadata = metadataResponse.Models.FirstOrDefault();
                if (metadata == null || !SyncDecisions.IsServerNewer(metadata.UpdatedAt, _settings.Current.LastSyncedAt))
                    return null;

                var response = await client.From<UserDataRow>().Get();
                row = response.Models.FirstOrDefault();
            }

            if (row?.TasksJson == null) return null;

            // Conflict resolution forces the server copy even when its timestamp is not newer.
            if (!force && !SyncDecisions.IsServerNewer(row.UpdatedAt, _settings.Current.LastSyncedAt))
                return null;

            var (data, readError) = ReadServerTasks(row);
            if (readError != null) return readError;
            if (data == null) return null;

            await _storage.SaveAsync(data);
            _settings.Current.LastSyncedAt = row.UpdatedAt;
            _settings.SaveDebounced();
            TasksReceived?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<SyncConflict?> CheckConflictAsync()
    {
        var client = _account.Client;
        if (!_account.IsSignedIn || client == null || _account.IsMfaChallengePending) return null;
        try
        {
            var localData = await _storage.LoadAsync();
            var response = await client.From<UserDataRow>().Get();
            var row = response.Models.FirstOrDefault();

            var (serverData, readError) = ReadServerTasks(row);
            // CheckAndHandleConflictAsync performs a pull next, which surfaces this read error without overwriting the row.
            if (readError != null) return null;

            int localTasks = localData.Tasks.Count(t => !t.IsDeleted);
            int localLists = localData.Lists.Count(l => !l.IsDeleted);
            int serverTasks = serverData?.Tasks.Count(t => !t.IsDeleted) ?? 0;
            int serverLists = serverData?.Lists.Count(l => !l.IsDeleted) ?? 0;

            // An account containing only tombstones has no live data to conflict with.
            bool localHasData = localTasks > 0 || localLists > 0;
            bool serverHasData = serverTasks > 0 || serverLists > 0;

            if (localHasData && serverHasData)
            {
                var localPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Hatch", "tasks.json");
                var localLastMod = File.Exists(localPath)
                    ? File.GetLastWriteTimeUtc(localPath)
                    : DateTime.MinValue;

                return new SyncConflict(
                    localTasks,
                    localLists,
                    localLastMod,
                    serverTasks,
                    serverLists,
                    row!.UpdatedAt);
            }

            if (localHasData)
                // Back up local data immediately when the account has no live data.
                await PushAsync(localData);

            return null;
        }
        catch { return null; }
    }

    public Task<string?> ResolveConflictAsync(SyncConflictResolution resolution) => resolution switch
    {
        SyncConflictResolution.UseLocal => ResolveConflictUseLocalAsync(),
        SyncConflictResolution.UseServer => ResolveConflictUseServerAsync(),
        _ => ResolveConflictMergeAsync()
    };

    private async Task<string?> ResolveConflictUseLocalAsync()
    {
        var data = await _storage.LoadAsync();
        // The explicit choice replaces server state, so this push skips merge-before-push.
        return await PushAsync(data, mergeFirst: false);
    }

    private Task<string?> ResolveConflictUseServerAsync()
        => PullIfNewerAsync(force: true);

    private async Task<string?> ResolveConflictMergeAsync()
    {
        var client = _account.Client;
        if (!_account.IsSignedIn || client == null) return Strings.Sync_Error_NotSignedIn;
        try
        {
            var local = await _storage.LoadAsync();
            var response = await client.From<UserDataRow>().Get();
            var row = response.Models.FirstOrDefault();
            var (server, readError) = ReadServerTasks(row);
            if (readError != null) return readError;

            // Unlike choosing a side, merge keeps records from both datasets.
            var merged = SyncMerge.Merge(local, server ?? new TasksFile());
            await _storage.SaveAsync(merged);
            TasksReceived?.Invoke();
            return await PushAsync(merged, mergeFirst: false);
        }
        catch (Exception ex) { return ex.Message; }
    }
}
