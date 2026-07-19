using MailLogInspector.App;
using System.Text.Json;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalReportDomScriptsTests
{
    [Fact]
    public void ReadScriptSupportsPortalListRowsWithoutDependingOnTables()
    {
        string script = SmtpPortalReportDomScripts.ReadFirstPageReports;

        Assert.Contains("NextGen_", script, StringComparison.Ordinal);
        Assert.Contains(".ant-list-item", script, StringComparison.Ordinal);
        Assert.Contains("[role=\"row\"]", script, StringComparison.Ordinal);
        Assert.Contains("Ready", script, StringComparison.Ordinal);
        Assert.Contains("querySelectorAll('body *')", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadScriptTargetsExactReportAndNeverNavigatesPagination()
    {
        const string reportName =
            "NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)";

        string script = SmtpPortalReportDomScripts.BuildDownloadClick(reportName);

        Assert.Contains(JsonSerializer.Serialize(reportName), script, StringComparison.Ordinal);
        Assert.Contains("download", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add CSV Report", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pagination", script, StringComparison.OrdinalIgnoreCase);
    }
}