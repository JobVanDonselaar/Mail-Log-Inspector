using System.Text.RegularExpressions;

namespace MailLogInspector.Storage;

public static partial class GmailReportMailParser
{
    private const string ExpectedSender = "no-reply@smtp.com";

    [GeneratedRegex(@"https://[^\s""'<>]+?\.zip(?:\?[^\s""'<>]*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ZipUrlRegex();

    public static bool TryExtractZipUrl(string sender, string? htmlBody, string? textBody, out string? zipUrl)
    {
        zipUrl = null;
        if (!string.Equals(sender, ExpectedSender, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string content = string.Join("\n", new[] { htmlBody, textBody }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        Match match = ZipUrlRegex().Match(content);
        if (!match.Success)
        {
            return false;
        }

        zipUrl = match.Value;
        return true;
    }
}
