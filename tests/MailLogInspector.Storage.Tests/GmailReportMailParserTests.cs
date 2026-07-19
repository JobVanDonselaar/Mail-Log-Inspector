using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class GmailReportMailParserTests
{
    [Fact]
    public void TryExtractZipUrl_ReturnsZipLink_ForSmtpSenderHtmlMail()
    {
        const string html = "<html><body><a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/test.zip\">Download Report</a></body></html>";

        bool success = GmailReportMailParser.TryExtractZipUrl("no-reply@smtp.com", html, null, out string? zipUrl);

        Assert.True(success);
        Assert.Equal("https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/test.zip", zipUrl);
    }

    [Fact]
    public void TryExtractZipUrl_ReturnsFalse_ForDifferentSender()
    {
        const string html = "<html><body><a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/test.zip\">Download Report</a></body></html>";

        bool success = GmailReportMailParser.TryExtractZipUrl("other@example.com", html, null, out string? zipUrl);

        Assert.False(success);
        Assert.Null(zipUrl);
    }

    [Fact]
    public void TryExtractZipUrl_ReturnsFalse_WhenBodyDoesNotContainDirectZipLink()
    {
        const string textBody = "Download Report: https://my.smtp.com/reporting?tab=reports";

        bool success = GmailReportMailParser.TryExtractZipUrl("no-reply@smtp.com", null, textBody, out string? zipUrl);

        Assert.False(success);
        Assert.Null(zipUrl);
    }
}
