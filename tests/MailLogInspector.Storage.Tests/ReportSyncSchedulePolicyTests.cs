using MailLogInspector.App;
using System;
using System.IO;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class ReportSyncSchedulePolicyTests
{
    private static readonly TimeZoneInfo AmsterdamSummerTime =
        TimeZoneInfo.CreateCustomTimeZone(
            "UTC+2",
            TimeSpan.FromHours(2),
            "UTC+2",
            "UTC+2");

    [Fact]
    public void BeforeOneAmDoesNotRun()
    {
        Assert.False(ReportSyncSchedulePolicy.ShouldRunAutomatic(
            enabled: true,
            activeArchiveMonthKey: null,
            latestReportDay: null,
            utcNow: new DateTime(2026, 7, 18, 22, 59, 0, DateTimeKind.Utc),
            AmsterdamSummerTime));
    }

    [Fact]
    public void AtOneAmRunsWhenYesterdayIsMissing()
    {
        Assert.True(ReportSyncSchedulePolicy.ShouldRunAutomatic(
            enabled: true,
            activeArchiveMonthKey: null,
            latestReportDay: new DateTime(2026, 7, 17),
            utcNow: new DateTime(2026, 7, 18, 23, 0, 0, DateTimeKind.Utc),
            AmsterdamSummerTime));
    }

    [Fact]
    public void DoesNotRunWhenYesterdayWasAlreadyImported()
    {
        Assert.False(ReportSyncSchedulePolicy.ShouldRunAutomatic(
            enabled: true,
            activeArchiveMonthKey: null,
            latestReportDay: new DateTime(2026, 7, 18),
            utcNow: new DateTime(2026, 7, 18, 23, 0, 0, DateTimeKind.Utc),
            AmsterdamSummerTime));
    }

    [Theory]
    [InlineData("2026-07-19T00:31:00", 840)]
    [InlineData("2026-07-19T00:59:30", 30)]
    [InlineData("2026-07-19T01:00:00", 900)]
    public void TimerDelayAlignsToNextQuarterHour(string localNowText, int expectedSeconds)
    {
        DateTime localNow = DateTime.Parse(localNowText);

        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            ReportSyncSchedulePolicy.DelayUntilNextQuarterHour(localNow));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "2026-06")]
    public void DisabledOrArchiveModeDoesNotRun(bool enabled, string? archiveMonth)
    {
        Assert.False(ReportSyncSchedulePolicy.ShouldRunAutomatic(
            enabled,
            archiveMonth,
            latestReportDay: null,
            utcNow: new DateTime(2026, 7, 18, 23, 0, 0, DateTimeKind.Utc),
            AmsterdamSummerTime));
    }

    [Fact]
    public void AutomaticTimerUsesTheReportDaySchedulePolicy()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.Gmail.cs"));

        Assert.Contains("ReadLatestDailyImportReportDayReadOnly", source, StringComparison.Ordinal);
        Assert.Contains("ReportSyncSchedulePolicy.ShouldRunAutomatic", source, StringComparison.Ordinal);
    }
}
