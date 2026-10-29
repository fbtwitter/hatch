using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;

namespace Hatch.Services;

[SupportedOSPlatform("windows")]
internal static class ProtectedSecretFile
{
    internal static string? Load(string folder, string resource, string name)
    {
        try
        {
            var bytes = File.ReadAllBytes(PathFor(folder, resource, name));
            var plain = ProtectedData.Unprotect(bytes, Entropy(resource, name), DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    internal static void Save(string folder, string resource, string name, string value)
    {
        Directory.CreateDirectory(folder);
        var path = PathFor(folder, resource, name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var plain = Encoding.UTF8.GetBytes(value);
        try
        {
            var encrypted = ProtectedData.Protect(plain, Entropy(resource, name), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static void Delete(string folder, string resource, string name)
    {
        try { File.Delete(PathFor(folder, resource, name)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
    }

    private static string PathFor(string folder, string resource, string name) => Path.Combine(folder,
        Convert.ToHexString(SHA256.HashData(Entropy(resource, name))) + ".bin");

    private static byte[] Entropy(string resource, string name) => Encoding.UTF8.GetBytes(resource + ":" + name);
}
