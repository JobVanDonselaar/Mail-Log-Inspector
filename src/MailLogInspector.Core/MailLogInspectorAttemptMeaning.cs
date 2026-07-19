using System;
using System.Linq;

namespace MailLogInspector.Core;

public static class MailLogInspectorAttemptMeaning
{
	public static MailLogInspectorReasonCode ClassifyReason(string? status, string? responseCode, string? responseMessage, string? bounceClass)
	{
		string normalizedStatus = status?.Trim() ?? string.Empty;
		string normalizedCode = responseCode?.Trim() ?? string.Empty;
		string combined = $"{responseMessage?.Trim()} {bounceClass?.Trim()}".ToLowerInvariant();
		if (string.Equals(normalizedStatus, "D", StringComparison.OrdinalIgnoreCase) || normalizedCode == "250")
		{
			return MailLogInspectorReasonCode.Delivered;
		}

		if (normalizedCode == "452" || normalizedCode == "552" || ContainsAny(combined, "mailbox full", "quota exceeded", "over quota", "mailbox is full", "out of storage"))
		{
			return MailLogInspectorReasonCode.MailboxFull;
		}

		if (ContainsAny(combined, "dns", "domain", "host not found", "unknown host", "resolving"))
		{
			return MailLogInspectorReasonCode.DnsProblem;
		}

		if (normalizedCode == "550" || ContainsAny(combined, "user unknown", "unknown user", "no such user", "invalid recipient", "bad-mailbox", "does not exist", "recipient address rejected"))
		{
			return MailLogInspectorReasonCode.InvalidRecipient;
		}

		if (ContainsAny(combined, "timeout", "timed out", "could not connect"))
		{
			return MailLogInspectorReasonCode.Timeout;
		}

		if (ContainsAny(combined, "message expired", "expired"))
		{
			return MailLogInspectorReasonCode.MessageExpired;
		}

		if (normalizedCode == "554" || ContainsAny(combined, "spam", "policy", "blocked", "blacklist", "blacklisted", "reputation", "not authorized", "denied", "relay denied"))
		{
			return MailLogInspectorReasonCode.PolicyBlock;
		}

		return MailLogInspectorReasonCode.Other;
	}

	public static MailLogInspectorBounceType ClassifyBounceType(string? bounceClass)
	{
		string value = bounceClass?.Trim().ToLowerInvariant() ?? string.Empty;
		if (value.Contains("hard", StringComparison.Ordinal))
		{
			return MailLogInspectorBounceType.Hard;
		}

		if (value.Contains("soft", StringComparison.Ordinal))
		{
			return MailLogInspectorBounceType.Soft;
		}

		if (value.Contains("block", StringComparison.Ordinal))
		{
			return MailLogInspectorBounceType.Block;
		}

		return value.Contains("undetermined", StringComparison.Ordinal) ? MailLogInspectorBounceType.Undetermined : MailLogInspectorBounceType.None;
	}

	public static string DescribeReason(MailLogInspectorReasonCode reason)
	{
		return reason switch
		{
			MailLogInspectorReasonCode.Delivered => "250 Afgeleverd",
			MailLogInspectorReasonCode.MailboxFull => "452 Mailbox vol",
			MailLogInspectorReasonCode.DnsProblem => "DNS probleem",
			MailLogInspectorReasonCode.InvalidRecipient => "550 Ongeldig",
			MailLogInspectorReasonCode.Timeout => "Timeout",
			MailLogInspectorReasonCode.MessageExpired => "Message expired",
			MailLogInspectorReasonCode.PolicyBlock => "Geblokkeerd policy/spam",
			_ => "Overig"
		};
	}

	public static string DescribeBounceStatus(MailLogInspectorReasonCode reason)
	{
		return reason switch
		{
			MailLogInspectorReasonCode.MailboxFull => "Mailbox vol",
			MailLogInspectorReasonCode.DnsProblem => "DNS probleem",
			MailLogInspectorReasonCode.InvalidRecipient => "Adres ongeldig",
			MailLogInspectorReasonCode.Timeout => "Timeout",
			MailLogInspectorReasonCode.MessageExpired => "Verlopen",
			MailLogInspectorReasonCode.PolicyBlock => "Policy block",
			_ => "Bounce"
		};
	}

	public static string DescribeResponseCode(string? responseCode)
	{
		return (responseCode?.Trim() ?? string.Empty) switch
		{
			"250" => "Geaccepteerd door ontvangende server",
			"421" => "Server tijdelijk niet beschikbaar",
			"450" => "Mailbox tijdelijk niet beschikbaar",
			"451" => "Tijdelijke verwerkingsfout op ontvangende server",
			"452" => "Onvoldoende opslag op ontvangende server",
			"501" => "Ongeldige adres- of commandosyntax",
			"511" => "Bericht verlopen of niet meer af te leveren",
			"550" => "Ontvangeradres ongeldig of geweigerd",
			"552" => "Mailbox vol of limiet overschreden",
			"554" => "Geblokkeerd door policy, spamfilter of inhoudscontrole",
			"557" => "Niet-standaard providerfout of beleidsweigering",
			"0" => "Geen bruikbare SMTP-code gevonden",
			"" => "Geen bruikbare SMTP-code gevonden",
			_ => "Onbekende SMTP-respons"
		};
	}

	private static bool ContainsAny(string value, params string[] needles) =>
		needles.Any(needle => value.Contains(needle, StringComparison.Ordinal));
}
