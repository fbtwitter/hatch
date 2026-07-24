namespace Hatch.Helpers;

// The text of the sync recovery kit, separated from the ViewModel so its content can be
// asserted. What this document says is the whole feature — a kit that fails to distinguish
// itself from the account password or an MFA code would leave a user feeling covered when
// they are not.
public static class RecoveryKit
{
    public static string Build(string passphrase, string email, DateTime createdAt) =>
        $"""
        HATCH SYNC RECOVERY KIT
        Created {createdAt:yyyy-MM-dd HH:mm}

        Account:    {email}
        Passphrase: {passphrase}

        WHAT THIS IS
        Your Hatch tasks are encrypted on your own device before they are uploaded.
        This passphrase is the only key. It is not stored on any server, so nobody --
        not Hatch, not the sync provider, not an administrator -- can look it up,
        reset it or recover it for you.

        Lose this passphrase and every synced task becomes permanently unreadable.
        There is no support route back. That is what "end-to-end encrypted" means.

        WHAT THIS IS NOT
        This is not your account password, and not a two-factor recovery code.
        Those get you back into the account. This is what makes the contents readable
        once you are in. You need both.

        WHERE TO KEEP IT
        Somewhere you will still have after this computer is gone: a password manager,
        a printout, or a file on separate storage. Keeping it only on this PC defeats
        the point -- that is the copy most likely to disappear with it.
        """;

    public static string FileName(DateTime createdAt) => $"hatch-recovery-kit-{createdAt:yyyy-MM-dd}";
}
