namespace MailLogInspector.App;

public enum AppActivationRequest
{
    Activate,
    Admin
}

public static class AppStartupRequestParser
{
    public static AppActivationRequest Parse(IReadOnlyList<string> arguments)
    {
        return arguments.Any(argument =>
            string.Equals(argument, "/admin", StringComparison.OrdinalIgnoreCase))
            ? AppActivationRequest.Admin
            : AppActivationRequest.Activate;
    }

    public static string ToPipeValue(AppActivationRequest request)
    {
        return request == AppActivationRequest.Admin ? "admin" : "activate";
    }

    public static AppActivationRequest ParsePipeValue(string? value)
    {
        return string.Equals(value, "admin", StringComparison.OrdinalIgnoreCase)
            ? AppActivationRequest.Admin
            : AppActivationRequest.Activate;
    }
}
