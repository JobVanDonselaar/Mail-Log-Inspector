using System.Collections.Generic;
using System.Linq;

namespace MailLogInspector.Core;

public sealed record MailLogInspectorAnalysisSummary(
	int TotalCount,
	int DeliveredCount,
	int UnderwayCount,
	int BounceCount,
	IReadOnlyList<MailLogInspectorBreakdownRow> SenderVolumeRows,
	IReadOnlyList<MailLogInspectorBreakdownRow> SenderLowestSuccessRows,
	IReadOnlyList<MailLogInspectorBreakdownRow> RecipientProblemVolumeRows,
	IReadOnlyList<MailLogInspectorBreakdownRow> RecipientHighestProblemRateRows,
	IReadOnlyList<MailLogInspectorValueMeaningCount> TopBounceCauses,
	IReadOnlyList<MailLogInspectorValueMeaningCount> TopResponseCodes)
{
	public IReadOnlyList<MailLogInspectorValueCount> TopSenderDomains =>
		SenderVolumeRows.Select(row => new MailLogInspectorValueCount(row.Key, row.Total)).ToList();

	public IReadOnlyList<MailLogInspectorValueCount> TopRecipientDomains =>
		RecipientProblemVolumeRows.Select(row => new MailLogInspectorValueCount(row.Key, row.Total)).ToList();
}
