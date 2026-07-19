using System.Text.Json;

namespace MailLogInspector.App;

public static class SmtpPortalScriptResultParser
{
    public static bool ParseBoolean(string? json)
    {
        return !string.IsNullOrWhiteSpace(json) &&
               JsonSerializer.Deserialize<bool?>(json) == true;
    }
}
