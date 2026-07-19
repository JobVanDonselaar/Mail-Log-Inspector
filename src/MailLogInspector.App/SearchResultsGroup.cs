using MailLogInspector.Core;

namespace MailLogInspector.App;

internal sealed class SearchResultsGroup
{
	public required string GroupKey { get; init; }

	public required string Sender { get; init; }

	public required IReadOnlyList<MailLogInspectorSearchRow> Rows { get; init; }

	public MailLogInspectorSearchRow LatestRow => Rows[0];

	public int DeliveredCount => Rows.Count(static row => string.Equals(row.Status, "afgeleverd", StringComparison.OrdinalIgnoreCase));

	public int UnderwayCount => Rows.Count(static row => string.Equals(row.Status, "onderweg", StringComparison.OrdinalIgnoreCase));

	public int BounceCount => Rows.Count(static row => string.Equals(row.Status, "bounce", StringComparison.OrdinalIgnoreCase));

	public string RecipientSummary => Rows.Count == 1 ? Rows[0].Recipient : $"{Rows.Count} ontvangers";

	public string StatusSummary
	{
		get
		{
			List<string> parts = new();
			if (DeliveredCount > 0)
			{
				parts.Add($"{DeliveredCount} afgeleverd");
			}

			if (BounceCount > 0)
			{
				parts.Add($"{BounceCount} bounce");
			}

			if (UnderwayCount > 0)
			{
				parts.Add($"{UnderwayCount} onderweg");
			}

			return parts.Count == 0 ? "-" : string.Join(", ", parts);
		}
	}

	public string DurationSummary => Rows.Count == 1 ? Rows[0].DurationDisplay : $"{Rows.Count} mails";
}
