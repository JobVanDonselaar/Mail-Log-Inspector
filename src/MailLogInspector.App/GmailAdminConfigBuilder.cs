using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed record GmailAdminSettingsInput(
    string AccountEmailAddress,
    string AuthenticationMode,
    string ClientId,
    string ClientSecret,
    string AppPassword,
    string ImapProvider = "gmail",
    string CustomImapHost = "",
    int? CustomImapPort = null,
    bool CustomImapUseSsl = true);

public static class GmailAdminConfigBuilder
{
    public const string StoredSecretPlaceholder = "********";
    public static GmailReportConfig Build(
        GmailReportConfig stored,
        GmailAdminSettingsInput input)
    {
        string imapProvider = ImapProvider.Normalize(input.ImapProvider);
        bool gmailOAuth = string.Equals(imapProvider, ImapProvider.Gmail, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(input.AuthenticationMode, GmailAuthenticationMode.OAuth, StringComparison.OrdinalIgnoreCase);
        string authenticationMode = gmailOAuth
            ? GmailAuthenticationMode.OAuth
            : GmailAuthenticationMode.AppPassword;
        ImapConnectionProfile profile = ImapProviderProfiles.Resolve(
            imapProvider,
            input.CustomImapHost,
            input.CustomImapPort,
            input.CustomImapUseSsl);
        string? clientSecret = string.IsNullOrWhiteSpace(input.ClientSecret)
            ? stored.ClientSecret
            : GmailOAuthService.ProtectClientSecret(input.ClientSecret.Trim());
        string? encryptedAppPassword = string.IsNullOrWhiteSpace(input.AppPassword) ||
                                     string.Equals(input.AppPassword, StoredSecretPlaceholder, StringComparison.Ordinal)
            ? stored.EncryptedAppPassword
            : GmailOAuthService.ProtectSecret(input.AppPassword.Trim());

        return stored with
        {
            AccountEmailAddress = input.AccountEmailAddress.Trim(),
            AuthenticationMode = authenticationMode,
            ClientId = input.ClientId.Trim(),
            ClientSecret = clientSecret,
            EncryptedAppPassword = encryptedAppPassword,
            ImapProvider = imapProvider,
            ImapHost = profile.Host,
            ImapPort = profile.Port,
            ImapUseSsl = profile.UseSsl
        };
    }
}