using System.Text.Json;
using Hatch.Models;
using Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

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
    // Anon key is intentionally public — Supabase RLS enforces per-user access.
    private const string SupabaseUrl = "https://cwgasedfewjarujvsesy.supabase.co";
    private const string SupabaseKey = "sb_publishable_b7RM0NcDWuL0rtHEeWeSHQ_95qa8uH5";

    private Client? _client;

    public bool IsSignedIn => _client?.Auth.CurrentSession != null;
    public string? UserEmail => _client?.Auth.CurrentUser?.Email;

    public event Action? StateChanged;

    public async Task InitializeAsync()
    {
        var options = new SupabaseOptions { AutoRefreshToken = true };
        _client = new Client(SupabaseUrl, SupabaseKey, options);
        await _client.InitializeAsync();
        await RestoreSessionAsync();
    }

    private async Task RestoreSessionAsync()
    {
        var access  = App.Settings.SyncAccessToken;
        var refresh = App.Settings.SyncRefreshToken;
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
        if (_client == null) return "Service not ready.";
        try
        {
            var session = await _client.Auth.SignIn(email, password);
            if (session?.AccessToken == null) return "Sign-in failed — check your credentials.";
            await PersistSessionAsync(session);
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Returns null on success, message on partial success (email confirmation needed).
    public async Task<string?> SignUpAsync(string email, string password)
    {
        if (_client == null) return "Service not ready.";
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
            return "Account created. Check your email to confirm before signing in.";
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task SignOutAsync()
    {
        try { await _client?.Auth.SignOut(); } catch { }
        ClearTokens();
        await App.SettingsService.SaveAsync();
        StateChanged?.Invoke();
    }

    public async Task PushAsync(TasksFile data)
    {
        if (!IsSignedIn || _client == null) return;
        var userId = _client.Auth.CurrentUser?.Id;
        if (string.IsNullOrEmpty(userId)) return;
        try
        {
            var json = JsonSerializer.Serialize(data);
            await _client.From<UserDataRow>().Upsert(new UserDataRow
            {
                UserId    = userId,
                TasksJson = json,
                UpdatedAt = DateTime.UtcNow
            });
            App.Settings.LastSyncedAt = DateTime.UtcNow;
            _ = App.SettingsService.SaveAsync();
            StateChanged?.Invoke();
        }
        catch { /* sync failure is silent — local data is always safe */ }
    }

    // Pulls server data only if the server has changes newer than the last local sync.
    public async Task<TasksFile?> PullIfNewerAsync()
    {
        if (!IsSignedIn || _client == null) return null;
        try
        {
            var response = await _client.From<UserDataRow>().Get();
            var row = response.Models.FirstOrDefault();
            if (row?.TasksJson == null) return null;

            // Skip if local is already up to date
            if (App.Settings.LastSyncedAt.HasValue &&
                row.UpdatedAt <= App.Settings.LastSyncedAt.Value)
                return null;

            return JsonSerializer.Deserialize<TasksFile>(row.TasksJson);
        }
        catch { return null; }
    }

    private async Task PersistSessionAsync(Supabase.Gotrue.Session session)
    {
        App.Settings.SyncAccessToken = session.AccessToken;
        App.Settings.SyncRefreshToken = session.RefreshToken;
        App.Settings.SyncUserEmail = _client?.Auth.CurrentUser?.Email;
        await App.SettingsService.SaveAsync();
    }

    private static void ClearTokens()
    {
        App.Settings.SyncAccessToken  = null;
        App.Settings.SyncRefreshToken = null;
        App.Settings.SyncUserEmail    = null;
        App.Settings.LastSyncedAt     = null;
    }
}
