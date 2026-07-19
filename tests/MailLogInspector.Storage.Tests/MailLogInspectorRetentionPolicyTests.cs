using MailLogInspector.Core;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorRetentionPolicyTests
{
	[Fact]
	public void ActiveRetentionDays_IsNinetyDays()
	{
		Assert.Equal(90, MailLogInspectorRetentionPolicy.ActiveRetentionDays);
	}

	[Fact]
	public void ActiveCutoffDate_ReturnsNinetyDaysBeforeToday()
	{
		DateTime today = new(2026, 7, 8);

		DateTime cutoff = MailLogInspectorRetentionPolicy.ActiveCutoffDate(today);

		Assert.Equal(new DateTime(2026, 4, 9), cutoff);
	}
}
