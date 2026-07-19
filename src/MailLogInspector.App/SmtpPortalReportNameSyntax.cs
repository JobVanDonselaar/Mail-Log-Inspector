using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MailLogInspector.App;

public sealed record SmtpPortalReportSyntaxValidation(
    bool IsValid,
    string? ErrorMessage);

public static class SmtpPortalReportNameSyntax
{
    public const string DefaultTemplate =
        "NextGen_{start}(00)_{end}(00) (delivered + bounced + queue) (raw_event_stream)";

    private const int MaximumTemplateLength = 300;
    private static readonly ConcurrentDictionary<string, Regex> RegexCache =
        new(StringComparer.Ordinal);

    public static SmtpPortalReportSyntaxValidation Validate(string? template)
    {
        string normalized = template?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return Invalid("Vul een rapportsyntax in.");
        }

        if (normalized.Length > MaximumTemplateLength)
        {
            return Invalid("De rapportsyntax mag maximaal 300 tekens bevatten.");
        }

        if (!HasBalancedBraces(normalized))
        {
            return Invalid("Gebruik geldige accolades rond placeholders.");
        }

        foreach (Match match in Regex.Matches(normalized, @"\{[^{}]*\}", RegexOptions.CultureInvariant))
        {
            if (!string.Equals(match.Value, "{start}", StringComparison.Ordinal) &&
                !string.Equals(match.Value, "{end}", StringComparison.Ordinal))
            {
                return Invalid($"Onbekende placeholder: {match.Value}.");
            }
        }

        if (CountOccurrences(normalized, "{start}") != 1)
        {
            return Invalid("Gebruik {start} exact één keer.");
        }

        if (CountOccurrences(normalized, "{end}") != 1)
        {
            return Invalid("Gebruik {end} exact één keer.");
        }

        return new SmtpPortalReportSyntaxValidation(true, null);
    }

    public static string ResolveTemplate(bool useDefault, string? customTemplate)
    {
        if (useDefault)
        {
            return DefaultTemplate;
        }

        string normalized = customTemplate?.Trim() ?? string.Empty;
        SmtpPortalReportSyntaxValidation validation = Validate(normalized);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.ErrorMessage);
        }

        return normalized;
    }

    public static string BuildExample(
        string template,
        DateTime? start = null,
        DateTime? end = null)
    {
        string normalized = ResolveTemplate(false, template);
        DateTime exampleStart = start?.Date ?? new DateTime(2026, 7, 17);
        DateTime exampleEnd = end?.Date ?? new DateTime(2026, 7, 18);
        return normalized
            .Replace("{start}", exampleStart.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
            .Replace("{end}", exampleEnd.ToString("yyyy-MM-dd"), StringComparison.Ordinal);
    }

    public static Regex BuildRegex(string template)
    {
        string normalized = ResolveTemplate(false, template);
        return RegexCache.GetOrAdd(normalized, static value =>
        {
            string pattern = Regex.Escape(value)
                .Replace(
                    Regex.Escape("{start}"),
                    @"(?<start>[0-9]{4}-[0-9]{2}-[0-9]{2})",
                    StringComparison.Ordinal)
                .Replace(
                    Regex.Escape("{end}"),
                    @"(?<end>[0-9]{4}-[0-9]{2}-[0-9]{2})",
                    StringComparison.Ordinal);
            return new Regex(
                "^" + pattern + "$",
                RegexOptions.CultureInvariant | RegexOptions.Compiled);
        });
    }

    private static SmtpPortalReportSyntaxValidation Invalid(string message)
    {
        return new SmtpPortalReportSyntaxValidation(false, message);
    }

    private static bool HasBalancedBraces(string value)
    {
        int depth = 0;
        foreach (char character in value)
        {
            if (character == '{')
            {
                depth++;
                if (depth > 1)
                {
                    return false;
                }
            }
            else if (character == '}')
            {
                if (depth == 0)
                {
                    return false;
                }

                depth--;
            }
        }

        return depth == 0;
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
