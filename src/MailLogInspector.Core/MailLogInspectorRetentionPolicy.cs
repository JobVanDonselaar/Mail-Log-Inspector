using System;

namespace MailLogInspector.Core;

public static class MailLogInspectorRetentionPolicy
{
	public const int ActiveRetentionDays = 90;

	public static DateTime ActiveCutoffDate(DateTime today) => today.Date.AddDays(-ActiveRetentionDays);
}
