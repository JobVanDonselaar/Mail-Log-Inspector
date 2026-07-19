using System;

namespace MailLogInspector.Core;

public sealed record MailLogInspectorSearchRow(DateTime? AcceptedAt, string Sender, string Recipient, string TrackingId, string Status, int? DurationSeconds, MailLogInspectorReasonCode ReasonCode, string LastMessage, DateTime FirstSeenAt, DateTime LastSeenAt, string SourceFileName)
{
	public string StatusDisplay
	{
		get
		{
			return NormalizeStatus(Status) switch
			{
				"afgeleverd" => "Afgeleverd",
				"onderweg" => "Onderweg",
				"bounce" => MailLogInspectorAttemptMeaning.DescribeBounceStatus(ReasonCode),
				_ => string.IsNullOrWhiteSpace(Status) ? "-" : Status
			};
		}
	}

	public string DurationDisplay
	{
		get
		{
			if (!DurationSeconds.HasValue || DurationSeconds.Value <= 0)
			{
				return "-";
			}
			TimeSpan timeSpan = TimeSpan.FromSeconds(DurationSeconds.Value);
			if (!(timeSpan.TotalHours >= 1.0))
			{
				if (!(timeSpan.TotalMinutes >= 1.0))
				{
					return $"{timeSpan.Seconds}s";
				}
				return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
			}
			return $"{(int)timeSpan.TotalHours}u {timeSpan.Minutes}m";
		}
	}

	private static string NormalizeStatus(string? status)
	{
		return string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();
	}
}
