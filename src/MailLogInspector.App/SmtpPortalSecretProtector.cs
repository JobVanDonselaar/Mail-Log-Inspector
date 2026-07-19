using System.Security.Cryptography;
using System.Text;

namespace MailLogInspector.App;

public static class SmtpPortalSecretProtector
{
    public static string Protect(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Empty;
        }

        byte[] plaintext = Encoding.UTF8.GetBytes(secret.Trim());
        return Convert.ToBase64String(ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string encryptedSecret)
    {
        if (string.IsNullOrWhiteSpace(encryptedSecret))
        {
            return string.Empty;
        }

        byte[] protectedBytes = Convert.FromBase64String(encryptedSecret);
        byte[] plaintext = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }
}
