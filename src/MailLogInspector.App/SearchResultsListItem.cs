using MailLogInspector.Core;

namespace MailLogInspector.App;

internal sealed class SearchResultsListItem
{
	public required string ItemKey { get; init; }

	public required bool IsGroup { get; init; }

	public required bool IsExpanded { get; set; }

	public required int Level { get; init; }

	public required string AcceptedDisplay { get; init; }

	public required string SenderDisplay { get; init; }

	public required string RecipientDisplay { get; init; }

	public required string StatusDisplay { get; init; }

	public required string DurationDisplay { get; init; }

	public required string CountDisplay { get; init; }

	public required int CountValue { get; init; }

	public required string StatusSummary { get; init; }

	public MailLogInspectorSearchRow? Row { get; init; }

	public SearchResultsGroup? Group { get; init; }

	public string ToggleGlyph => IsGroup ? (IsExpanded ? "▼" : "▶") : string.Empty;
}
