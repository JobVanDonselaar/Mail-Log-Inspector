namespace MailLogInspector.Storage;

public sealed record SmtpPortalConfig(
    string? Username,
    string? EncryptedPassword,
    string? EncryptedTotpSecret,
    string? ConnectionStatus,
    DateTime? LastProbeAtUtc,
    bool UseDefaultReportSyntax = true,
    string? CustomReportSyntax = null,
    DateTime? LastSuccessfulPortalUseAtUtc = null)
{
    public static SmtpPortalConfig Empty { get; } = new(null, null, null, null, null);
}