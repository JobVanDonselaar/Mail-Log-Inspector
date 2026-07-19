namespace MailLogInspector.App;

public static class SmtpPortalAuthenticationTiming
{
    private static readonly TimeSpan CredentialResultWait = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TotpResultWait = TimeSpan.FromSeconds(5);

    public static bool IsCredentialSubmissionPending(DateTime submittedAtUtc, DateTime nowUtc)
    {
        return nowUtc - submittedAtUtc < CredentialResultWait;
    }

    public static bool IsTotpSubmissionPending(DateTime submittedAtUtc, DateTime nowUtc)
    {
        return nowUtc - submittedAtUtc < TotpResultWait;
    }
}
