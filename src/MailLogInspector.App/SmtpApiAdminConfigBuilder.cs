using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed record SmtpApiAdminSettingsInput(
    string ApiKey,
    string Channel,
    string ReportSyntax1,
    string ReportSyntax2,
    string ReportSyntax3);

public static class SmtpApiAdminConfigBuilder
{
    public const string StoredSecretPlaceholder = "********";

    public static SmtpApiConfig Build(SmtpApiConfig stored, SmtpApiAdminSettingsInput input)
    {
        SmtpApiReportSyntaxSetValidation validation = SmtpApiReportSyntaxSet.Validate(
            input.ReportSyntax1,
            input.ReportSyntax2,
            input.ReportSyntax3);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.ErrorMessage!);
        }

        return stored with
        {
            EncryptedApiKey = IsStoredSecretPlaceholderOrEmpty(input.ApiKey)
                ? stored.EncryptedApiKey
                : SmtpPortalSecretProtector.Protect(input.ApiKey),
            Channel = Normalize(input.Channel),
            ReportSyntax1 = Normalize(input.ReportSyntax1),
            ReportSyntax2 = Normalize(input.ReportSyntax2),
            ReportSyntax3 = Normalize(input.ReportSyntax3)
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsStoredSecretPlaceholderOrEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, StoredSecretPlaceholder, StringComparison.Ordinal);
    }
}
