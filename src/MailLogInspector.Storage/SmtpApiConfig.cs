namespace MailLogInspector.Storage;

public sealed record SmtpApiConfig(
    string? EncryptedApiKey,
    string? Channel,
    string? ReportSyntax1,
    string? ReportSyntax2,
    string? ReportSyntax3,
    string? ConnectionStatus,
    DateTime? LastSuccessfulUseAtUtc)
{
    public static SmtpApiConfig Empty { get; } = new(null, null, null, null, null, null, null);

    public bool HasApiKey => !string.IsNullOrWhiteSpace(EncryptedApiKey);
}
