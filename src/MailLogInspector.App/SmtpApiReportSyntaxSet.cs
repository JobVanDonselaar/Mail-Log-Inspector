namespace MailLogInspector.App;

public sealed record SmtpApiReportSyntaxSetValidation(
    bool IsValid,
    int SlotNumber,
    string? ErrorMessage)
{
    public static SmtpApiReportSyntaxSetValidation Valid { get; } = new(true, 0, null);
}

public static class SmtpApiReportSyntaxSet
{
    public const int SlotCount = 3;

    public static IReadOnlyList<string> Resolve(params string?[] templates)
    {
        List<string> resolved = [];
        foreach (string? template in templates ?? [])
        {
            string normalized = template?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                continue;
            }

            if (!SmtpPortalReportNameSyntax.Validate(normalized).IsValid)
            {
                continue;
            }

            if (!resolved.Contains(normalized, StringComparer.Ordinal))
            {
                resolved.Add(normalized);
            }
        }

        return resolved.Count == 0
            ? [SmtpPortalReportNameSyntax.DefaultTemplate]
            : resolved;
    }

    public static SmtpApiReportSyntaxSetValidation Validate(params string?[] templates)
    {
        string?[] slots = templates ?? [];
        bool hasAny = false;
        for (int index = 0; index < slots.Length; index++)
        {
            string normalized = slots[index]?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                continue;
            }

            hasAny = true;
            SmtpPortalReportSyntaxValidation validation =
                SmtpPortalReportNameSyntax.Validate(normalized);
            if (!validation.IsValid)
            {
                return new SmtpApiReportSyntaxSetValidation(
                    false,
                    index + 1,
                    $"Syntax {index + 1}: {validation.ErrorMessage}");
            }
        }

        return hasAny
            ? SmtpApiReportSyntaxSetValidation.Valid
            : new SmtpApiReportSyntaxSetValidation(
                false,
                1,
                "Vul minimaal één rapportsyntax in.");
    }
}
