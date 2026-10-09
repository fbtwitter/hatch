using System.Text.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace Hatch.Tests.Integration;

[TestClass]
public sealed class SyncFailureTests
{
    private LoopbackServer _server = null!;
    private SettingsService _settings = null!;
    private TaskStorageService _storage = null!;
    private SyncAccountService _account = null!;
    private SyncService _sync = null!;
    private int _received;
    private readonly DateTime _lastSync = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [TestInitialize]
    public async Task SetUp()
    {
        _server = new();
        var folder = Path.Combine(Path.GetTempPath(), "Hatch.Integration", Guid.NewGuid().ToString("N"));
        _storage = new(folder);
        _settings = new(folder);
        _settings.Current.LastSyncedAt = _lastSync;
        await _storage.SaveAsync(new TasksFile { Tasks = [new TodoItem { Title = "Local must survive" }] });
        SyncPassphraseStore.Save("synthetic test passphrase");
        var client = new Supabase.Client(_server.Url, "synthetic-key", new Supabase.SupabaseOptions { AutoRefreshToken = false });
        client.Auth.SetPersistence(new MemorySession());
        client.Auth.LoadSession();
        _account = new() { Client = client };
        _sync = new(_settings, _account, _storage);
        _sync.TasksReceived += () => _received++;
    }

    [TestCleanup]
    public async Task TearDown()
    {
        _sync.StopAutoSync();
        _settings.FlushPendingSave();
        SyncPassphraseStore.Clear();
        await _server.DisposeAsync();
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(429)]
    [DataRow(503)]
    public async Task FailedPull_PreservesLocalDataAndLastSuccessfulTimestamp(int status)
    {
        _server.Respond = _ => (status, "{\"message\":\"simulated remote failure\"}");
        var error = await _sync.PullIfNewerAsync(force: true);
        Assert.IsFalse(string.IsNullOrEmpty(error));
        Assert.AreEqual(1, _server.Requests.Count, "The real SDK must reach the simulated transport.");
        await AssertLocalUnchanged();
    }

    [TestMethod]
    public async Task CorruptNewerRemote_BlocksPushBeforeAnyWrite()
    {
        _server.Respond = _ => (200, Row("{truncated"));
        var error = await _sync.PushAsync(await _storage.LoadAsync());
        Assert.AreEqual("WrongPassphrase", error);
        Assert.AreEqual(2, _server.Requests.Count, "Read metadata, then read payload.");
        Assert.IsTrue(_server.Requests.All(r => r.StartsWith("GET ")));
        await AssertLocalUnchanged();
    }

    [TestMethod]
    public async Task FailedPush_DoesNotClaimSyncSuccess()
    {
        _server.Respond = _ => (503, "{\"message\":\"unavailable\"}");
        var error = await _sync.PushAsync(await _storage.LoadAsync(), mergeFirst: false);
        Assert.IsFalse(string.IsNullOrEmpty(error));
        Assert.IsTrue(_server.Requests.Single().StartsWith("POST "));
        await AssertLocalUnchanged();
    }

    [TestMethod]
    public async Task MfaPending_BlocksBothTransferDirectionsBeforeNetworkAccess()
    {
        _account.IsMfaChallengePending = true;
        Assert.AreEqual("MfaRequired", await _sync.PullIfNewerAsync(force: true));
        Assert.AreEqual("MfaRequired", await _sync.PushAsync(await _storage.LoadAsync()));
        Assert.AreEqual(0, _server.Requests.Count);
        await AssertLocalUnchanged();
    }

    [TestMethod]
    public async Task RetryAfterFailedPull_StoresRecoveredPayloadAndNotifiesOnce()
    {
        _server.Respond = _ => (503, "{\"message\":\"unavailable\"}");
        Assert.IsNotNull(await _sync.PullIfNewerAsync(force: true));
        var remote = new TasksFile { Tasks = [new TodoItem { Title = "Recovered remote" }] };
        _server.Respond = _ => (200, Row(SyncWire.Serialize(remote)));
        Assert.IsNull(await _sync.PullIfNewerAsync(force: true));
        Assert.AreEqual("Recovered remote", (await _storage.LoadAsync()).Tasks.Single().Title);
        Assert.AreEqual(_lastSync.AddDays(1), _settings.Current.LastSyncedAt);
        Assert.AreEqual(1, _received);
    }

    private string Row(string payload) => JsonSerializer.Serialize(new[]
    {
        new { user_id = "test-user", tasks_json = payload, updated_at = _lastSync.AddDays(1) }
    });

    [TestMethod]
    public async Task FailedMetadataRead_BlocksPushWithoutReplacingTheRemoteRow()
    {
        _server.Respond = _ => (503, "{\"message\":\"unavailable\"}");
        Assert.IsNotNull(await _sync.PushAsync(await _storage.LoadAsync()));
        Assert.IsTrue(_server.Requests.Single().StartsWith("GET "));
        await AssertLocalUnchanged();
    }

    [TestMethod]
    public async Task WrongPassphraseOnPull_DoesNotReplaceLocalTasks()
    {
        var remote = SyncCrypto.Encrypt(SyncWire.Serialize(new TasksFile()), "different passphrase", SyncCrypto.CreateSalt());
        _server.Respond = _ => (200, Row(remote));
        Assert.AreEqual("WrongPassphrase", await _sync.PullIfNewerAsync(force: true));
        await AssertLocalUnchanged();
    }

    [TestMethod]
    public async Task FailedPassphraseChange_RestoresThePreviousLocalKey()
    {
        var (oldPassphrase, salt) = SyncPassphraseStore.Load();
        var remote = SyncCrypto.Encrypt(SyncWire.Serialize(await _storage.LoadAsync()), oldPassphrase!, salt!);
        _server.Respond = request => request.StartsWith("GET ")
            ? (200, Row(remote)) : (503, "{\"message\":\"unavailable\"}");
        Assert.IsNotNull(await _sync.ChangePassphraseAsync(oldPassphrase!, "replacement passphrase"));
        Assert.AreEqual(oldPassphrase, SyncPassphraseStore.Load().Passphrase);
        Assert.AreEqual(2, _server.Requests.Count);
        await AssertLocalUnchanged();
    }

    private async Task AssertLocalUnchanged()
    {
        Assert.AreEqual("Local must survive", (await _storage.LoadAsync()).Tasks.Single().Title);
        Assert.AreEqual(_lastSync, _settings.Current.LastSyncedAt);
        Assert.AreEqual(0, _received);
    }

    private sealed class MemorySession : IGotrueSessionPersistence<Session>
    {
        public void SaveSession(Session session) { }
        public void DestroySession() { }
        public Session LoadSession() => new()
        {
            AccessToken = "synthetic-token", RefreshToken = "synthetic-refresh",
            User = new User { Id = "test-user" }
        };
    }
}
