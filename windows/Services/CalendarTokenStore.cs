using System.Security.Cryptography;
using System.Text;
using Hatch.Helpers;
using Hatch.Models;
using Windows.Security.Credentials;

namespace Hatch.Services;

internal static class CalendarTokenStore
{
    private const int ElementNotFound = unchecked((int)0x80070490);
    private const int PathNotFound = unchecked((int)0x80070003);
    private static readonly string TokenFolder = Path.Combine(AppDataPath.Folder, "calendar-credentials");

    private static string Resource(CalendarAccount account) => account.Provider switch
    {
        CalendarProvider.Microsoft => "Hatch.Calendar.Microsoft",
        CalendarProvider.Google => "Hatch.Calendar.Google",
        _ => throw new InvalidOperationException("Unknown calendar provider.")
    };

    internal static string? Load(CalendarAccount account)
    {
        if (AppDataPath.IsUiTest || account.Provider == CalendarProvider.Ics) return null;

        try
        {
            if (LoadProtectedFile(account) is { } savedToken) return savedToken;
            var credential = Find(account);
            if (credential == null) return null;
            credential.RetrievePassword();
            var legacyToken = credential.Password;
            SaveProtectedFile(account, legacyToken);
            RemoveLegacyCredential(account);
            return legacyToken;
        }
        catch (Exception ex) when (IsCredentialStoreError(ex))
        {
            throw new CalendarCredentialStoreException(ex);
        }
    }

    internal static void Save(CalendarAccount account, string refreshToken)
    {
        if (AppDataPath.IsUiTest) throw new InvalidOperationException("Calendar credentials are disabled in UI tests.");

        try
        {
            SaveProtectedFile(account, refreshToken);
        }
        catch (Exception ex) when (IsCredentialStoreError(ex))
        {
            throw new CalendarCredentialStoreException(ex);
        }

        RemoveLegacyCredential(account);
    }

    internal static void Clear(CalendarAccount account)
    {
        if (AppDataPath.IsUiTest || account.Provider == CalendarProvider.Ics) return;

        try { File.Delete(ProtectedPath(account)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception ex) when (IsCredentialStoreError(ex)) { throw new CalendarCredentialStoreException(ex); }

        RemoveLegacyCredential(account);
    }

    private static PasswordCredential? Find(CalendarAccount account)
    {
        try { return new PasswordVault().Retrieve(Resource(account), account.Id); }
        // An uninitialized per-user Credential Locker can report path-not-found
        // instead of element-not-found when the first credential is queried.
        catch (Exception ex) when (ex.HResult is ElementNotFound or PathNotFound) { return null; }
    }

    private static void RemoveLegacyCredential(CalendarAccount account)
    {
        try
        {
            if (Find(account) is { } credential) new PasswordVault().Remove(credential);
        }
        catch (Exception ex) when (ex.HResult == PathNotFound) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            CrashLog.Write("Calendar credential migration", "A legacy Credential Locker entry could not be removed.");
        }
    }

    private static string? LoadProtectedFile(CalendarAccount account)
    {
        byte[] encrypted;
        try { encrypted = File.ReadAllBytes(ProtectedPath(account)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }

        var decrypted = ProtectedData.Unprotect(encrypted, Entropy(account), DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(decrypted); }
        finally { CryptographicOperations.ZeroMemory(decrypted); }
    }

    private static void SaveProtectedFile(CalendarAccount account, string refreshToken)
    {
        Directory.CreateDirectory(TokenFolder);
        var path = ProtectedPath(account);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var plaintext = Encoding.UTF8.GetBytes(refreshToken);
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, Entropy(account), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string ProtectedPath(CalendarAccount account) => Path.Combine(TokenFolder,
        Convert.ToHexString(SHA256.HashData(Entropy(account))) + ".bin");

    private static byte[] Entropy(CalendarAccount account) =>
        Encoding.UTF8.GetBytes(Resource(account) + ":" + account.Id);

    private static bool IsCredentialStoreError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or CryptographicException;
}

internal sealed class CalendarCredentialStoreException(Exception innerException)
    : Exception("Calendar credentials could not be accessed.", innerException);
