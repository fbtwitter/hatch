using Hatch.Models;
using Hatch.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class SyncDecisionsTests
{
    private const string Passphrase = "correct horse battery";

    private static string Envelope(string passphrase, params string[] titles)
    {
        var file = new TasksFile();
        foreach (var t in titles)
            file.Tasks.Add(new TodoItem { Title = t });
        return SyncCrypto.Encrypt(SyncWire.Serialize(file), passphrase, SyncCrypto.CreateSalt());
    }

    // ── The unreadable-row rule (docs/sync-protocol.md §2) ──────────────────────────────
    // These four are the ones that matter. "Cannot read" must never be reported as "empty",
    // because callers treat empty as licence to push over the top of it.

    [TestMethod]
    public void Encrypted_row_without_a_passphrase_is_not_empty()
    {
        var result = SyncDecisions.ReadServerPayload(Envelope(Passphrase, "buy milk"), passphrase: null);

        Assert.AreEqual(ServerReadStatus.NeedsPassphrase, result.Status);
        Assert.AreNotEqual(ServerReadStatus.Empty, result.Status);
        Assert.IsNull(result.Data);
    }

    [TestMethod]
    public void Encrypted_row_with_the_wrong_passphrase_is_unreadable_not_empty()
    {
        var result = SyncDecisions.ReadServerPayload(Envelope(Passphrase, "buy milk"), "wrong passphrase");

        Assert.AreEqual(ServerReadStatus.Unreadable, result.Status);
        Assert.AreNotEqual(ServerReadStatus.Empty, result.Status);
        Assert.IsNull(result.Data);
    }

    [TestMethod]
    public void Corrupt_plaintext_is_unreadable_not_empty()
    {
        var result = SyncDecisions.ReadServerPayload("{\"Tasks\": [ truncated", Passphrase);

        Assert.AreEqual(ServerReadStatus.Unreadable, result.Status);
        Assert.IsNull(result.Data);
    }

    [TestMethod]
    public void Tampered_envelope_is_unreadable_not_empty()
    {
        var envelope = Envelope(Passphrase, "buy milk");
        // Flip a character in the ciphertext segment; GCM authentication must reject it.
        var parts = envelope.Split('.');
        parts[4] = (parts[4][0] == 'A' ? "B" : "A") + parts[4][1..];
        var tampered = string.Join('.', parts);

        var result = SyncDecisions.ReadServerPayload(tampered, Passphrase);

        Assert.AreEqual(ServerReadStatus.Unreadable, result.Status);
    }

    // ── Genuinely empty ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void No_row_is_empty()
        => Assert.AreEqual(ServerReadStatus.Empty, SyncDecisions.ReadServerPayload(null, Passphrase).Status);

    [TestMethod]
    public void Empty_payload_is_empty()
        => Assert.AreEqual(ServerReadStatus.Empty, SyncDecisions.ReadServerPayload("", Passphrase).Status);

    // ── Readable ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Encrypted_row_with_the_right_passphrase_round_trips()
    {
        var result = SyncDecisions.ReadServerPayload(Envelope(Passphrase, "buy milk", "walk dog"), Passphrase);

        Assert.AreEqual(ServerReadStatus.Ok, result.Status);
        Assert.AreEqual(2, result.Data!.Tasks.Count);
        Assert.AreEqual("buy milk", result.Data.Tasks[0].Title);
    }

    [TestMethod]
    public void Legacy_plaintext_row_is_read_without_a_passphrase()
    {
        var file = new TasksFile();
        file.Tasks.Add(new TodoItem { Title = "predates encryption" });

        var result = SyncDecisions.ReadServerPayload(SyncWire.Serialize(file), passphrase: null);

        Assert.AreEqual(ServerReadStatus.Ok, result.Status);
        Assert.AreEqual("predates encryption", result.Data!.Tasks[0].Title);
    }

    // ── Staleness ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Never_synced_always_pulls()
        => Assert.IsTrue(SyncDecisions.IsServerNewer(new DateTime(2026, 1, 1), lastSyncedAt: null));

    [TestMethod]
    public void Server_ahead_of_last_sync_pulls()
        => Assert.IsTrue(SyncDecisions.IsServerNewer(new DateTime(2026, 7, 2), new DateTime(2026, 7, 1)));

    [TestMethod]
    public void Server_behind_last_sync_does_not_pull()
        => Assert.IsFalse(SyncDecisions.IsServerNewer(new DateTime(2026, 6, 30), new DateTime(2026, 7, 1)));

    [TestMethod]
    public void Identical_timestamp_does_not_pull()
    {
        // We already hold this exact revision; re-pulling would churn the file for nothing.
        var t = new DateTime(2026, 7, 1, 12, 0, 0);
        Assert.IsFalse(SyncDecisions.IsServerNewer(t, t));
    }

    // ── MFA challenge ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Password_only_session_on_an_enrolled_account_is_challenged()
        => Assert.IsTrue(SyncDecisions.IsMfaChallengePending("aal1", "aal2"));

    [TestMethod]
    public void Session_already_at_aal2_is_not_challenged()
        => Assert.IsFalse(SyncDecisions.IsMfaChallengePending("aal2", "aal2"));

    [TestMethod]
    public void Account_without_a_factor_is_never_challenged()
        => Assert.IsFalse(SyncDecisions.IsMfaChallengePending("aal1", "aal1"));

    [TestMethod]
    public void Null_current_level_counts_as_aal1()
        // Supabase documents "aal1 (or null)" for a conventional login.
        => Assert.IsTrue(SyncDecisions.IsMfaChallengePending(null, "aal2"));

    [TestMethod]
    public void Null_next_level_is_not_challenged()
        => Assert.IsFalse(SyncDecisions.IsMfaChallengePending(null, null));

    // ── OAuth callback parsing ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Callback_code_is_extracted()
    {
        var p = SyncDecisions.ParseQueryString("code=abc123&state=xyz");

        Assert.AreEqual("abc123", p["code"]);
        Assert.AreEqual("xyz", p["state"]);
    }

    [TestMethod]
    public void Callback_values_are_url_decoded()
    {
        var p = SyncDecisions.ParseQueryString("error_description=Access%20was%20denied");

        Assert.AreEqual("Access was denied", p["error_description"]);
    }

    [TestMethod]
    public void Callback_value_containing_equals_is_kept_whole()
    {
        // Base64url codes can end in '='; splitting on every '=' would truncate them.
        var p = SyncDecisions.ParseQueryString("code=YWJjZA==");

        Assert.AreEqual("YWJjZA==", p["code"]);
    }

    [TestMethod]
    public void Malformed_callback_pairs_are_skipped_not_thrown()
    {
        var p = SyncDecisions.ParseQueryString("&&novalue&code=ok&");

        Assert.AreEqual(1, p.Count);
        Assert.AreEqual("ok", p["code"]);
    }

    // ── Supabase URL normalization ─────────────────────────────────────────────────────
    // The /rest/v1/ suffix shipped once and failed only at runtime, as an opaque
    // "No API key found in request".

    [TestMethod]
    public void Rest_path_suffix_is_stripped()
        => Assert.AreEqual("https://abc.supabase.co",
            SyncDecisions.NormalizeSupabaseUrl("https://abc.supabase.co/rest/v1/"));

    [TestMethod]
    public void Trailing_slash_is_stripped()
        => Assert.AreEqual("https://abc.supabase.co",
            SyncDecisions.NormalizeSupabaseUrl("https://abc.supabase.co/"));

    [TestMethod]
    public void Surrounding_whitespace_is_stripped()
        => Assert.AreEqual("https://abc.supabase.co",
            SyncDecisions.NormalizeSupabaseUrl("  https://abc.supabase.co  "));

    [TestMethod]
    public void Correct_url_is_left_alone()
        => Assert.AreEqual("https://abc.supabase.co",
            SyncDecisions.NormalizeSupabaseUrl("https://abc.supabase.co"));
}
