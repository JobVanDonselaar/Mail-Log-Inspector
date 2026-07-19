namespace MailLogInspector.Core;

public sealed record MailLogInspectorDurationDistribution(
    int DurationCount,
    int MissingCount,
    int WithinOneMinute,
    int OneToFiveMinutes,
    int FiveToFifteenMinutes,
    int FifteenToSixtyMinutes,
    int OverOneHour)
{
    public static MailLogInspectorDurationDistribution Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public int LongerThanOneMinute =>
        OneToFiveMinutes + FiveToFifteenMinutes + FifteenToSixtyMinutes + OverOneHour;

    public int LongerThanFiveMinutes => FiveToFifteenMinutes + FifteenToSixtyMinutes + OverOneHour;

    public int LongerThanFifteenMinutes => FifteenToSixtyMinutes + OverOneHour;
}
