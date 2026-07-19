using System;
using System.Globalization;

namespace MailLogInspector.App;

public static class MailLogInspectorDisplayFormats
{
    public const string DateTimePattern = "dd-MM-yyyy HH:mm";
    public const string DatePattern = "dd-MM-yyyy";

    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("nl-NL");

    public static string DateTime(DateTime value)
    {
        return value.ToString(DateTimePattern, Culture);
    }

    public static string DateTime(DateTime? value)
    {
        return value.HasValue ? DateTime(value.Value) : "-";
    }

    public static string DateTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString(DateTimePattern, Culture);
    }

    public static string Date(DateTime value)
    {
        return value.ToString(DatePattern, Culture);
    }

    public static string Date(DateTime? value)
    {
        return value.HasValue ? Date(value.Value) : "-";
    }
}