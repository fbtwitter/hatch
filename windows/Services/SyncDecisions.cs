using Hatch.Models;

namespace Hatch.Services;

public enum ServerReadStatus
{
    // No row, or a row with an empty payload. Distinct from Unreadable: this genuinely
    // means "the account has nothing", and only this status may be treated as empty.
    Empty,
    Ok,
    // Encrypted, and no passphrase is available to try.
    NeedsPassphrase,
    // Encrypted and the passphrase did not open it, or the plaintext did not parse.
    // docs/sync-protocol.md §2: never overwrite a row in this state.
    Unreadable,
}

public readonly record struct ServerReadResult(ServerReadStatus Status, TasksFile? Data);

// The decisions SyncService makes, separated from the I/O it makes them about.
//
// Every rule here can lose a user's data if it is wrong, and none of it could be tested
// while it lived inside a class that reaches App.Settings and the Credential Locker.
// Nothing in this file may reference WinUI or WinRT — it is linked into Hatch.Tests.Unit,
// which targets plain net10.0 on purpose.
public static class SyncDecisions
{
    public static ServerReadResult ReadServerPayload(string? tasksJson, string? passphrase)
    {
        if (string.IsNullOrEmpty(tasksJson)) return new(ServerReadStatus.Empty, null);

        var json = tasksJson;
        if (SyncCrypto.IsEncrypted(json))
        {
            if (passphrase == null) return new(ServerReadStatus.NeedsPassphrase, null);
            var plain = SyncCrypto.TryDecrypt(json, passphrase);
            if (plain == null) return new(ServerReadStatus.Unreadable, null);
            json = plain;
        }

        // Plaintext that fails to parse is unreadable too, not empty — a truncated or
        // corrupt row must never license an overwrite.
        try { return new(ServerReadStatus.Ok, SyncWire.Deserialize(json)); }
        catch { return new(ServerReadStatus.Unreadable, null); }
    }

    // Whether a pull is worth doing. Never synced means always fetch; equal timestamps mean
    // we already have this exact revision.
    public static bool IsServerNewer(DateTime serverUpdatedAt, DateTime? lastSyncedAt)
        => lastSyncedAt == null || serverUpdatedAt > lastSyncedAt.Value;

    // A verified factor exists (next level is aal2) but this session has not reached it.
    // Null current level is treated as aal1, matching Supabase: "aal1 (or null) means the
    // user's identity has been verified only with a conventional login".
    public static bool IsMfaChallengePending(string? currentAal, string? nextAal)
        => string.Equals(nextAal, "aal2", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(currentAal, "aal2", StringComparison.OrdinalIgnoreCase);

    public static Dictionary<string, string> ParseQueryString(string query)
        => query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(
                    p => Uri.UnescapeDataString(p[0]),
                    p => Uri.UnescapeDataString(p[1]));

    // The Supabase client appends its own REST path. A URL that already carries one
    // produces /rest/v1/rest/v1/ and fails as "No API key found in request" — which shipped
    // once, compiled clean, and only surfaced at runtime. Mirrors normalizeUrl in
    // mobile/shared SyncClient.kt, which guarded this first.
    public static string NormalizeSupabaseUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^"/rest/v1".Length].TrimEnd('/');
        return trimmed;
    }
}
