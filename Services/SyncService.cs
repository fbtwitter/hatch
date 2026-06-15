using System.Text.Json;
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

    public bool IsSignedIn => _client?.Auth?.CurrentSession != null;
    public string? UserEmail => _client?.Auth?.CurrentUser?.Email;

    public event Action? StateChanged;
    public event Action? TasksReceived; // fires when a pull returned newer data

    public async Task InitializeAsync()
    {
        var options = new Supabase.SupabaseOptions { AutoRefreshToken = true };
        _client = new SupabaseClient(SupabaseUrl, SupabaseKey, options);
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
        try { await (_client?.Auth?.SignOut() ?? Task.CompletedTask); } catch { }
        ClearTokens();
        await App.SettingsService.SaveAsync();
        StateChanged?.Invoke();
    }

    // Returns null on success, error message on failure.
    public async Task<string?> PushAsync(TasksFile data)
    {
        if (_client == null) return "Sync service not ready.";
        if (!IsSignedIn)    return "Not signed in.";
        var userId = _client.Auth.CurrentUser?.Id;
        if (string.IsNullOrEmpty(userId)) return "Could not read user ID.";
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
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Returns the GitHub OAuth URL to open in the browser, or null on failure.
    public async Task<string?> GetGoogleSignInUrlAsync()
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

    // Called when the app is activated via hatch://auth-callback after Google OAuth.
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

            var data = JsonSerializer.Deserialize<TasksFile>(row.TasksJson);
            if (data == null) return null;

            await new TaskStorageService().SaveAsync(data);
            App.Settings.LastSyncedAt = row.UpdatedAt;
            _ = App.SettingsService.SaveAsync();
            TasksReceived?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
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
