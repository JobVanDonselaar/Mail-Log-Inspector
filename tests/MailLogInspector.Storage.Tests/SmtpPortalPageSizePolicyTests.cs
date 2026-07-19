using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalPageSizePolicyTests
{
    private static readonly DateTime Today = new(2026, 7, 18);

    [Theory]
    [InlineData(null, null)]
    [InlineData("2026-07-17", null)]
    [InlineData("2026-07-15", null)]
    [InlineData("2026-07-14", 100)]
    public void Resolve_LeavesNormalPageSizeUntouchedAndUsesHundredAfterThreeDays(string? latestDayText, int? expected)
    {
        DateTime? latestDay = latestDayText is null ? null : DateTime.Parse(latestDayText);

        Assert.Equal(expected, SmtpPortalPageSizePolicy.Resolve(latestDay, Today));
    }
}
