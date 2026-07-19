using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed record SmtpPortalAdminSettingsInput(
    string Username,
    string Password,
    string TotpSecret,
    bool UseDefaultReportSyntax = true,
    string CustomReportSyntax = "");

public static class SmtpPortalAdminConfigBuilder
{
    public const string StoredSecretPlaceholder = "********";

    public static SmtpPortalConfig Build(
        SmtpPortalConfig stored,
        SmtpPortalAdminSettingsInput input)
    {
        string? customSyntax = stored.CustomReportSyntax;
        if (!input.UseDefaultReportSyntax)
        {
            customSyntax = SmtpPortalReportNameSyntax.ResolveTemplate(
                useDefault: false,
                input.CustomReportSyntax);
        }
        else if (!string.IsNullOrWhiteSpace(input.CustomReportSyntax))
        {
            SmtpPortalReportSyntaxValidation validation =
                SmtpPortalReportNameSyntax.Validate(input.CustomReportSyntax);
            if (validation.IsValid)
            {
                customSyntax = input.CustomReportSyntax.Trim();
            }
        }

        return stored with
        {
            Username = input.Username.Trim(),
            EncryptedPassword = IsStoredSecretPlaceholderOrEmpty(input.Password)
                ? stored.EncryptedPassword
                : SmtpPortalSecretProtector.Protect(input.Password),
            EncryptedTotpSecret = IsStoredSecretPlaceholderOrEmpty(input.TotpSecret)
                ? stored.EncryptedTotpSecret
                : SmtpPortalSecretProtector.Protect(input.TotpSecret),
            UseDefaultReportSyntax = input.UseDefaultReportSyntax,
            CustomReportSyntax = customSyntax
        };
    }

    private static bool IsStoredSecretPlaceholderOrEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, StoredSecretPlaceholder, StringComparison.Ordinal);
    }
}