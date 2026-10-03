using System.Security.Cryptography;
using System.Text;

namespace FlowOps.Api.Data;

internal static class LocalDevelopmentSecrets
{
    internal static string? ReadDatabasePassword(IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() || !OperatingSystem.IsWindows()) return null;

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowOps", "Secrets", "Development.dbpassword.dpapi");
        if (!File.Exists(path)) return null;

        try
        {
            var encrypted = File.ReadAllBytes(path);
            var plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                $"Cannot decrypt the local FlowOps database password at {path}. " +
                "It must be created by the Windows account running the API.", exception);
        }
    }
}
