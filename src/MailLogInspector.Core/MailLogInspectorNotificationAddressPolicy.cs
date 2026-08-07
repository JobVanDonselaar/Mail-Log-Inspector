using System.Globalization;

namespace MailLogInspector.Core;

/// <summary>
/// Bepaalt naar welk adres een bouncemelding gestuurd moet worden. Afzenders zoals
/// noreply@ kunnen zelf geen post ontvangen; daarvoor wordt info@ op hetzelfde domein voorgesteld.
/// </summary>
public static class MailLogInspectorNotificationAddressPolicy
{
    public const string FallbackLocalPart = "info";

    private static readonly string[] UnattendedLocalParts =
    [
        "noreply",
        "no-reply",
        "no_reply",
        "donotreply",
        "do-not-reply",
        "do_not_reply",
        "nepasrepondre",
        "bounce",
        "bounces",
        "mailer-daemon",
        "postmaster"
    ];

    /// <summary>Geeft true als het adres een geautomatiseerd afzenderadres is dat geen antwoord aanneemt.</summary>
    public static bool IsUnattendedSender(string? address)
    {
        string local = ExtractLocalPart(address);
        if (local.Length == 0)
        {
            return false;
        }

        string normalized = Normalize(local);
        return UnattendedLocalParts.Any(candidate =>
            string.Equals(normalized, Normalize(candidate), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Stelt het ontvangeradres voor: het afzenderadres zelf, of info@domein wanneer de afzender
    /// een geautomatiseerd adres is. Geeft een lege tekst terug als er geen bruikbaar domein is.
    /// </summary>
    public static string SuggestRecipient(string? senderAddress)
    {
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            return string.Empty;
        }

        string trimmed = senderAddress.Trim();
        string domain = ExtractDomain(trimmed);
        if (domain.Length == 0)
        {
            return string.Empty;
        }

        return IsUnattendedSender(trimmed)
            ? $"{FallbackLocalPart}@{domain}"
            : trimmed.ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>Controleert of een adres bruikbaar is als ontvanger.</summary>
    public static bool IsPlausibleAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        string trimmed = address.Trim();
        int at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
        {
            return false;
        }

        string domain = trimmed[(at + 1)..];
        return domain.Contains('.', StringComparison.Ordinal) &&
               !domain.StartsWith('.') &&
               !domain.EndsWith('.') &&
               !trimmed.Any(char.IsWhiteSpace);
    }

    private static string ExtractLocalPart(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        string trimmed = address.Trim();
        int at = trimmed.IndexOf('@');
        return at > 0 ? trimmed[..at] : trimmed;
    }

    private static string ExtractDomain(string address)
    {
        int at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1
            ? address[(at + 1)..].ToLower(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    /// <summary>Verwijdert scheidingstekens zodat no-reply, no_reply en noreply gelijk zijn.</summary>
    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }
}
