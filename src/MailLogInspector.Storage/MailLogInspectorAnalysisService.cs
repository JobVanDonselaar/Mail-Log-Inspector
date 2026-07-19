using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using MailLogInspector.Core;

namespace MailLogInspector.Storage;

public sealed class MailLogInspectorAnalysisService
{
	private readonly MailLogInspectorStore _store;

	public MailLogInspectorAnalysisService(MailLogInspectorStore store)
	{
		_store = store;
	}

	public MailLogInspectorAnalysisSummary BuildSummary(DateTime fromInclusive, DateTime throughInclusive, int topDomainLimit = 10, CancellationToken cancellationToken = default)
	{
		return BuildSummary(new MailLogInspectorSearchCriteria(fromInclusive, throughInclusive, null, null, null, null, null), topDomainLimit, cancellationToken);
	}

	public MailLogInspectorAnalysisSummary BuildSummary(MailLogInspectorSearchCriteria criteria, int limit = 10, CancellationToken cancellationToken = default)
	{
		using SqliteConnection connection = _store.OpenConnection();
		FilterScope scope = ResolveFilterScope(connection, criteria);
		if (HasUnresolvedDomain(scope.SenderParts.Domain, criteria.SenderDomain, scope.SenderDomainId) ||
			HasUnresolvedDomain(scope.RecipientParts.Domain, criteria.RecipientDomain, scope.RecipientDomainId))
		{
			return EmptySummary();
		}

		cancellationToken.ThrowIfCancellationRequested();
		(int totalCount, int deliveredCount, int underwayCount, int bounceCount) = ReadTotals(connection, criteria, scope, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<MailLogInspectorBreakdownRow> senderVolumeRows = ReadBreakdownRows(connection, criteria, scope, sender: true, BreakdownOrder.TotalVolume, limit, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<MailLogInspectorBreakdownRow> senderLowestSuccessRows = ReadBreakdownRows(connection, criteria, scope, sender: true, BreakdownOrder.LowestSuccessRate, limit, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<MailLogInspectorBreakdownRow> recipientProblemVolumeRows = ReadBreakdownRows(connection, criteria, scope, sender: false, BreakdownOrder.HighestProblemCount, limit, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<MailLogInspectorBreakdownRow> recipientHighestProblemRateRows = ReadBreakdownRows(connection, criteria, scope, sender: false, BreakdownOrder.HighestProblemRate, limit, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<MailLogInspectorValueMeaningCount> topBounceCauses = ReadTopBounceCauses(connection, criteria, scope, limit, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		IReadOnlyList<MailLogInspectorValueMeaningCount> topResponseCodes = ReadTopResponseCodes(connection, criteria, scope, limit, cancellationToken);
		return new MailLogInspectorAnalysisSummary(
			totalCount,
			deliveredCount,
			underwayCount,
			bounceCount,
			senderVolumeRows,
			senderLowestSuccessRows,
			recipientProblemVolumeRows,
			recipientHighestProblemRateRows,
			topBounceCauses,
			topResponseCodes);
	}

	private static (int TotalCount, int DeliveredCount, int UnderwayCount, int BounceCount) ReadTotals(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, FilterScope scope, CancellationToken cancellationToken)
	{
		if (CanUseDailyAggregates(criteria, scope) && criteria.Status is null)
		{
			string tableName = "analysis_daily_status";
			string? domainPredicate = null;
			long? domainId = null;
			if (scope.SenderDomainId.HasValue && !scope.RecipientDomainId.HasValue)
			{
				tableName = "analysis_daily_sender_domain";
				domainPredicate = " AND domain_id = $domainId";
				domainId = scope.SenderDomainId;
			}
			else if (scope.RecipientDomainId.HasValue && !scope.SenderDomainId.HasValue)
			{
				tableName = "analysis_daily_recipient_domain";
				domainPredicate = " AND domain_id = $domainId";
				domainId = scope.RecipientDomainId;
			}

			if (tableName == "analysis_daily_status")
			{
				using SqliteCommand aggregateCommand = connection.CreateCommand();
				aggregateCommand.CommandText = "SELECT SUM(total),\n       SUM(CASE WHEN status = 1 THEN total ELSE 0 END),\n       SUM(CASE WHEN status = 2 THEN total ELSE 0 END),\n       SUM(CASE WHEN status = 3 THEN total ELSE 0 END)\nFROM analysis_daily_status\nWHERE day_key >= $fromDay AND day_key <= $throughDay;";
				AddDayParameters(aggregateCommand, criteria);
				aggregateCommand.CommandTimeout = 30;
				using CancellationTokenRegistration aggregateCancellationRegistration = cancellationToken.Register(aggregateCommand.Cancel);
				using SqliteDataReader aggregateReader = aggregateCommand.ExecuteReader();
				aggregateReader.Read();
				return (ReadInt32(aggregateReader, 0), ReadInt32(aggregateReader, 1), ReadInt32(aggregateReader, 2), ReadInt32(aggregateReader, 3));
			}

			using SqliteCommand domainCommand = connection.CreateCommand();
			domainCommand.CommandText = $"SELECT SUM(total), SUM(delivered), SUM(underway), SUM(bounce)\nFROM {tableName}\nWHERE day_key >= $fromDay AND day_key <= $throughDay{domainPredicate};";
			AddDayParameters(domainCommand, criteria);
			domainCommand.Parameters.AddWithValue("$domainId", domainId!.Value);
			domainCommand.CommandTimeout = 30;
			using CancellationTokenRegistration domainCancellationRegistration = cancellationToken.Register(domainCommand.Cancel);
			using SqliteDataReader domainReader = domainCommand.ExecuteReader();
			domainReader.Read();
			return (ReadInt32(domainReader, 0), ReadInt32(domainReader, 1), ReadInt32(domainReader, 2), ReadInt32(domainReader, 3));
		}

		using SqliteCommand command = CreateFilteredCommand(connection, criteria, scope,
			"SELECT COUNT(*),\n" +
			"       SUM(CASE WHEN item.status = 1 THEN 1 ELSE 0 END),\n" +
			"       SUM(CASE WHEN item.status = 2 THEN 1 ELSE 0 END),\n" +
			"       SUM(CASE WHEN item.status = 3 THEN 1 ELSE 0 END)\n" +
			"FROM mail_items AS item\n" +
			"JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id\n" +
			"JOIN mail_addresses AS recipient ON recipient.address_id = item.recipient_address_id\n" +
			"WHERE item.accepted_at >= $fromInclusive\n" +
			"  AND item.accepted_at <= $throughInclusive\n" +
			"  AND ($senderLocal IS NULL OR sender.local_part = $senderLocal)\n" +
			"  AND ($recipientLocal IS NULL OR recipient.local_part = $recipientLocal)\n" +
			"  AND ($senderDomainId IS NULL OR item.sender_domain_id = $senderDomainId)\n" +
			"  AND ($recipientDomainId IS NULL OR item.recipient_domain_id = $recipientDomainId)\n" +
			"  AND ($status IS NULL OR item.status = $status);");
		command.CommandTimeout = 30;
		using CancellationTokenRegistration detailCancellationRegistration = cancellationToken.Register(command.Cancel);
		using SqliteDataReader reader = command.ExecuteReader();
		reader.Read();
		return (ReadInt32(reader, 0), ReadInt32(reader, 1), ReadInt32(reader, 2), ReadInt32(reader, 3));
	}

	private static IReadOnlyList<MailLogInspectorBreakdownRow> ReadBreakdownRows(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, FilterScope scope, bool sender, BreakdownOrder order, int limit, CancellationToken cancellationToken)
	{
		if (CanUseBreakdownAggregates(criteria, scope, sender))
		{
			return ReadAggregateBreakdownRows(connection, criteria, scope, sender, order, limit, cancellationToken);
		}

		string domainAlias = sender ? "sender_domain" : "recipient_domain";
		string domainColumn = sender ? "item.sender_domain_id" : "item.recipient_domain_id";
		string orderSql = order switch
		{
			BreakdownOrder.TotalVolume => "ORDER BY total DESC, delivered DESC, key ASC",
			BreakdownOrder.LowestSuccessRate => "ORDER BY CASE WHEN total = 0 THEN 1.0 ELSE CAST(delivered AS REAL) / total END ASC, total DESC, key ASC",
			BreakdownOrder.HighestProblemCount => "ORDER BY (underway + bounce) DESC, total DESC, key ASC",
			BreakdownOrder.HighestProblemRate => "ORDER BY CASE WHEN total = 0 THEN 0.0 ELSE CAST((underway + bounce) AS REAL) / total END DESC, total DESC, key ASC",
			_ => "ORDER BY total DESC, key ASC"
		};

		using SqliteCommand command = CreateFilteredCommand(connection, criteria, scope,
			$"SELECT key, total, delivered, underway, bounce\n" +
			"FROM (\n" +
			$"    SELECT COALESCE({domainAlias}.domain_name, '(geen domein)') AS key,\n" +
			"           COUNT(*) AS total,\n" +
			"           SUM(CASE WHEN item.status = 1 THEN 1 ELSE 0 END) AS delivered,\n" +
			"           SUM(CASE WHEN item.status = 2 THEN 1 ELSE 0 END) AS underway,\n" +
			"           SUM(CASE WHEN item.status = 3 THEN 1 ELSE 0 END) AS bounce\n" +
			"    FROM mail_items AS item\n" +
			"    JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id\n" +
			"    JOIN mail_addresses AS recipient ON recipient.address_id = item.recipient_address_id\n" +
			$"    LEFT JOIN mail_domains AS {domainAlias} ON {domainAlias}.domain_id = {domainColumn}\n" +
			"    WHERE item.accepted_at >= $fromInclusive\n" +
			"      AND item.accepted_at <= $throughInclusive\n" +
			"      AND ($senderLocal IS NULL OR sender.local_part = $senderLocal)\n" +
			"      AND ($recipientLocal IS NULL OR recipient.local_part = $recipientLocal)\n" +
			"      AND ($senderDomainId IS NULL OR item.sender_domain_id = $senderDomainId)\n" +
			"      AND ($recipientDomainId IS NULL OR item.recipient_domain_id = $recipientDomainId)\n" +
			"      AND ($status IS NULL OR item.status = $status)\n" +
			"    GROUP BY COALESCE(" + domainAlias + ".domain_name, '(geen domein)')\n" +
			")\n" +
			orderSql +
			"\nLIMIT $limit;");
		command.Parameters.AddWithValue("$limit", limit);

		command.CommandTimeout = 30;

		using CancellationTokenRegistration detailCancellationRegistration = cancellationToken.Register(command.Cancel);

		using SqliteDataReader reader = command.ExecuteReader();
		List<MailLogInspectorBreakdownRow> rows = new();
		while (((DbDataReader)(object)reader).Read())
		{
			rows.Add(new MailLogInspectorBreakdownRow(
				((DbDataReader)(object)reader).GetString(0),
				ReadInt32(reader, 1),
				ReadInt32(reader, 2),
				ReadInt32(reader, 3),
				ReadInt32(reader, 4)));
		}

		return rows;
	}

	private static IReadOnlyList<MailLogInspectorValueMeaningCount> ReadTopBounceCauses(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, FilterScope scope, int limit, CancellationToken cancellationToken)
	{
		if (CanUseReasonAggregates(criteria, scope))
		{
			using SqliteCommand aggregateCommand = connection.CreateCommand();
			aggregateCommand.CommandText = "SELECT reason_code, SUM(total)\nFROM analysis_daily_reason\nWHERE day_key >= $fromDay\n  AND day_key <= $throughDay\n  AND reason_code <> 1\nGROUP BY reason_code\nORDER BY SUM(total) DESC, reason_code ASC\nLIMIT $limit;";
			AddDayParameters(aggregateCommand, criteria);
			aggregateCommand.Parameters.AddWithValue("$limit", limit);
			aggregateCommand.CommandTimeout = 30;
			using CancellationTokenRegistration aggregateCancellationRegistration = cancellationToken.Register(aggregateCommand.Cancel);
			using SqliteDataReader aggregateReader = aggregateCommand.ExecuteReader();
			List<MailLogInspectorValueMeaningCount> aggregateRows = new();
			while (aggregateReader.Read())
			{
				string explanation = MailLogInspectorStore.DescribeReason(aggregateReader.GetInt32(0));
				aggregateRows.Add(new MailLogInspectorValueMeaningCount(explanation, ReadInt32(aggregateReader, 1), explanation));
			}
			return aggregateRows;
		}

		using SqliteCommand command = CreateFilteredCommand(connection, criteria with { Status = "bounce" }, scope,
			"SELECT item.reason_code,\n" +
			"       COUNT(*)\n" +
			"FROM mail_items AS item\n" +
			"JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id\n" +
			"JOIN mail_addresses AS recipient ON recipient.address_id = item.recipient_address_id\n" +
			"WHERE item.accepted_at >= $fromInclusive\n" +
			"  AND item.accepted_at <= $throughInclusive\n" +
			"  AND ($senderLocal IS NULL OR sender.local_part = $senderLocal)\n" +
			"  AND ($recipientLocal IS NULL OR recipient.local_part = $recipientLocal)\n" +
			"  AND ($senderDomainId IS NULL OR item.sender_domain_id = $senderDomainId)\n" +
			"  AND ($recipientDomainId IS NULL OR item.recipient_domain_id = $recipientDomainId)\n" +
			"  AND ($status IS NULL OR item.status = $status)\n" +
			"GROUP BY item.reason_code;");
		command.CommandTimeout = 30;
		using CancellationTokenRegistration detailCancellationRegistration = cancellationToken.Register(command.Cancel);
		using SqliteDataReader reader = command.ExecuteReader();
		Dictionary<string, int> counts = new(StringComparer.Ordinal);
		while (((DbDataReader)(object)reader).Read())
		{
			string explanation = MailLogInspectorStore.DescribeReason(((DbDataReader)(object)reader).GetInt32(0));
			counts[explanation] = counts.TryGetValue(explanation, out int count)
				? count + ReadInt32(reader, 1)
				: ReadInt32(reader, 1);
		}

		return counts
			.OrderByDescending(pair => pair.Value)
			.ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.Take(limit)
			.Select(pair => new MailLogInspectorValueMeaningCount(pair.Key, pair.Value, pair.Key))
			.ToList();
	}

	private static IReadOnlyList<MailLogInspectorValueMeaningCount> ReadTopResponseCodes(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, FilterScope scope, int limit, CancellationToken cancellationToken)
	{
		if (CanUseReasonAggregates(criteria, scope))
		{
			using SqliteCommand aggregateCommand = connection.CreateCommand();
			aggregateCommand.CommandText = "SELECT response_code, SUM(total)\nFROM analysis_daily_response\nWHERE day_key >= $fromDay\n  AND day_key <= $throughDay\nGROUP BY response_code\nORDER BY SUM(total) DESC, response_code ASC\nLIMIT $limit;";
			AddDayParameters(aggregateCommand, criteria);
			aggregateCommand.Parameters.AddWithValue("$limit", limit);
			aggregateCommand.CommandTimeout = 30;
			using CancellationTokenRegistration aggregateCancellationRegistration = cancellationToken.Register(aggregateCommand.Cancel);
			using SqliteDataReader aggregateReader = aggregateCommand.ExecuteReader();
			List<MailLogInspectorValueMeaningCount> aggregateRows = new();
			while (aggregateReader.Read())
			{
				string code = Convert.ToString(aggregateReader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "0";
				aggregateRows.Add(new MailLogInspectorValueMeaningCount(code, ReadInt32(aggregateReader, 1), MailLogInspectorAttemptMeaning.DescribeResponseCode(code)));
			}
			return aggregateRows;
		}

		using SqliteCommand command = CreateFilteredCommand(connection, criteria, scope,
			"SELECT code, COUNT(*)\n" +
			"FROM (\n" +
			"    SELECT COALESCE(item.response_code, 0) AS code\n" +
			"    FROM mail_items AS item\n" +
			"    JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id\n" +
			"    JOIN mail_addresses AS recipient ON recipient.address_id = item.recipient_address_id\n" +
			"    WHERE item.accepted_at >= $fromInclusive\n" +
			"      AND item.accepted_at <= $throughInclusive\n" +
			"      AND ($senderLocal IS NULL OR sender.local_part = $senderLocal)\n" +
			"      AND ($recipientLocal IS NULL OR recipient.local_part = $recipientLocal)\n" +
			"      AND ($senderDomainId IS NULL OR item.sender_domain_id = $senderDomainId)\n" +
			"      AND ($recipientDomainId IS NULL OR item.recipient_domain_id = $recipientDomainId)\n" +
			"      AND ($status IS NULL OR item.status = $status)\n" +
			")\n" +
			"GROUP BY code\n" +
			"ORDER BY COUNT(*) DESC, code ASC\n" +
			"LIMIT $limit;");
		command.Parameters.AddWithValue("$limit", limit);

		command.CommandTimeout = 30;

		using CancellationTokenRegistration detailCancellationRegistration = cancellationToken.Register(command.Cancel);

		using SqliteDataReader reader = command.ExecuteReader();
		List<MailLogInspectorValueMeaningCount> rows = new();
		while (((DbDataReader)(object)reader).Read())
		{
			string code = Convert.ToString(((DbDataReader)(object)reader).GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "0";
			rows.Add(new MailLogInspectorValueMeaningCount(code, ReadInt32(reader, 1), MailLogInspectorAttemptMeaning.DescribeResponseCode(code)));
		}

		return rows;
	}

	private static SqliteCommand CreateFilteredCommand(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, FilterScope scope, string sql)
	{
		SqliteCommand command = connection.CreateCommand();
		((DbCommand)(object)command).CommandText = sql;
		command.Parameters.AddWithValue("$fromInclusive", criteria.FromInclusive.Ticks);
		command.Parameters.AddWithValue("$throughInclusive", criteria.ThroughInclusive.Ticks);
		command.Parameters.AddWithValue("$senderLocal", DbValue(scope.SenderParts.LocalPart));
		command.Parameters.AddWithValue("$recipientLocal", DbValue(scope.RecipientParts.LocalPart));
		command.Parameters.AddWithValue("$senderDomainId", DbValue(scope.SenderDomainId));
		command.Parameters.AddWithValue("$recipientDomainId", DbValue(scope.RecipientDomainId));
		command.Parameters.AddWithValue("$status", DbValue(StatusToCode(criteria.Status)));
		return command;
	}

	private static IReadOnlyList<MailLogInspectorBreakdownRow> ReadAggregateBreakdownRows(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, FilterScope scope, bool sender, BreakdownOrder order, int limit, CancellationToken cancellationToken)
	{
		string tableName = sender ? "analysis_daily_sender_domain" : "analysis_daily_recipient_domain";
		long? domainId = sender ? scope.SenderDomainId : scope.RecipientDomainId;
		string domainPredicate = domainId.HasValue ? " AND aggregate.domain_id = $domainId" : string.Empty;
		string orderSql = order switch
		{
			BreakdownOrder.TotalVolume => "ORDER BY total DESC, delivered DESC, key ASC",
			BreakdownOrder.LowestSuccessRate => "ORDER BY CASE WHEN total = 0 THEN 1.0 ELSE CAST(delivered AS REAL) / total END ASC, total DESC, key ASC",
			BreakdownOrder.HighestProblemCount => "ORDER BY (underway + bounce) DESC, total DESC, key ASC",
			BreakdownOrder.HighestProblemRate => "ORDER BY CASE WHEN total = 0 THEN 0.0 ELSE CAST((underway + bounce) AS REAL) / total END DESC, total DESC, key ASC",
			_ => "ORDER BY total DESC, key ASC"
		};

		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = $"SELECT key, total, delivered, underway, bounce\nFROM (\n    SELECT COALESCE(domain.domain_name, '(geen domein)') AS key,\n           SUM(aggregate.total) AS total,\n           SUM(aggregate.delivered) AS delivered,\n           SUM(aggregate.underway) AS underway,\n           SUM(aggregate.bounce) AS bounce\n    FROM {tableName} AS aggregate\n    LEFT JOIN mail_domains AS domain ON domain.domain_id = aggregate.domain_id\n    WHERE aggregate.day_key >= $fromDay\n      AND aggregate.day_key <= $throughDay{domainPredicate}\n    GROUP BY COALESCE(domain.domain_name, '(geen domein)')\n)\n{orderSql}\nLIMIT $limit;";
		AddDayParameters(command, criteria);
		if (domainId.HasValue)
		{
			command.Parameters.AddWithValue("$domainId", domainId.Value);
		}
		command.Parameters.AddWithValue("$limit", limit);

		command.CommandTimeout = 30;

		using CancellationTokenRegistration detailCancellationRegistration = cancellationToken.Register(command.Cancel);

		using SqliteDataReader reader = command.ExecuteReader();
		List<MailLogInspectorBreakdownRow> rows = new();
		while (reader.Read())
		{
			rows.Add(new MailLogInspectorBreakdownRow(
				reader.GetString(0),
				ReadInt32(reader, 1),
				ReadInt32(reader, 2),
				ReadInt32(reader, 3),
				ReadInt32(reader, 4)));
		}

		return rows;
	}

	private static bool CanUseDailyAggregates(MailLogInspectorSearchCriteria criteria, FilterScope scope)
	{
		return IsWholeDayRange(criteria) &&
			scope.SenderParts.LocalPart is null &&
			scope.RecipientParts.LocalPart is null &&
			!(scope.SenderDomainId.HasValue && scope.RecipientDomainId.HasValue);
	}

	private static bool CanUseBreakdownAggregates(MailLogInspectorSearchCriteria criteria, FilterScope scope, bool sender)
	{
		if (!CanUseDailyAggregates(criteria, scope) || criteria.Status is not null)
		{
			return false;
		}

		return sender
			? !scope.RecipientDomainId.HasValue
			: !scope.SenderDomainId.HasValue;
	}

	private static bool CanUseReasonAggregates(MailLogInspectorSearchCriteria criteria, FilterScope scope)
	{
		return IsWholeDayRange(criteria) &&
			criteria.Status is null &&
			scope.SenderParts.LocalPart is null &&
			scope.RecipientParts.LocalPart is null &&
			!scope.SenderDomainId.HasValue &&
			!scope.RecipientDomainId.HasValue;
	}

	private static bool IsWholeDayRange(MailLogInspectorSearchCriteria criteria)
	{
		return criteria.FromInclusive.TimeOfDay == TimeSpan.Zero &&
			criteria.ThroughInclusive.TimeOfDay >= new TimeSpan(23, 59, 0);
	}

	private static void AddDayParameters(SqliteCommand command, MailLogInspectorSearchCriteria criteria)
	{
		command.Parameters.AddWithValue("$fromDay", criteria.FromInclusive.Ticks / TimeSpan.TicksPerDay);
		command.Parameters.AddWithValue("$throughDay", criteria.ThroughInclusive.Ticks / TimeSpan.TicksPerDay);
	}

	private static long? ResolveDomainId(SqliteConnection connection, string? domain)
	{
		if (string.IsNullOrWhiteSpace(domain))
		{
			return null;
		}

		using SqliteCommand command = connection.CreateCommand();
		((DbCommand)(object)command).CommandText = "SELECT domain_id FROM mail_domains WHERE domain_name = $domain LIMIT 1;";
		command.Parameters.AddWithValue("$domain", domain.Trim().ToLowerInvariant());
		object? result = ((DbCommand)(object)command).ExecuteScalar();
		return result is null ? null : Convert.ToInt64(result);
	}

	private static (string? LocalPart, string? Domain) SplitEmail(string? value)
	{
		string? normalized = Normalize(value);
		if (normalized is null)
		{
			return (null, null);
		}

		int at = normalized.LastIndexOf('@');
		if (at <= 0 || at == normalized.Length - 1)
		{
			return (normalized, null);
		}

		return (normalized[..at], normalized[(at + 1)..]);
	}

	private static string? Normalize(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
	}

	private static bool HasUnresolvedDomain(string? exactAddressDomain, string? explicitDomain, long? resolvedDomainId)
	{
		return (exactAddressDomain ?? Normalize(explicitDomain)) is not null && !resolvedDomainId.HasValue;
	}

	private static int? StatusToCode(string? status)
	{
		string? normalized = Normalize(status);
		return normalized is null ? null : MailLogInspectorStore.ToStatusCode(normalized);
	}

	private static FilterScope ResolveFilterScope(SqliteConnection connection, MailLogInspectorSearchCriteria criteria)
	{
		var senderParts = SplitEmail(criteria.Sender);
		var recipientParts = SplitEmail(criteria.Recipient);
		string? senderDomain = senderParts.Domain ?? Normalize(criteria.SenderDomain);
		string? recipientDomain = recipientParts.Domain ?? Normalize(criteria.RecipientDomain);
		return new FilterScope(
			senderParts,
			recipientParts,
			ResolveDomainId(connection, senderDomain),
			ResolveDomainId(connection, recipientDomain));
	}

	private static MailLogInspectorAnalysisSummary EmptySummary()
	{
		return new MailLogInspectorAnalysisSummary(
			0,
			0,
			0,
			0,
			Array.Empty<MailLogInspectorBreakdownRow>(),
			Array.Empty<MailLogInspectorBreakdownRow>(),
			Array.Empty<MailLogInspectorBreakdownRow>(),
			Array.Empty<MailLogInspectorBreakdownRow>(),
			Array.Empty<MailLogInspectorValueMeaningCount>(),
			Array.Empty<MailLogInspectorValueMeaningCount>());
	}

	private static int ReadInt32(SqliteDataReader reader, int ordinal)
	{
		return ((DbDataReader)(object)reader).IsDBNull(ordinal)
			? 0
			: Convert.ToInt32(((DbDataReader)(object)reader).GetValue(ordinal));
	}

	private static object DbValue(string? value) => value is null ? DBNull.Value : value;

	private static object DbValue(long? value) => value.HasValue ? value.Value : DBNull.Value;

	private static object DbValue(int? value) => value.HasValue ? value.Value : DBNull.Value;

	private readonly record struct FilterScope(
		(string? LocalPart, string? Domain) SenderParts,
		(string? LocalPart, string? Domain) RecipientParts,
		long? SenderDomainId,
		long? RecipientDomainId);

	private enum BreakdownOrder
	{
		TotalVolume,
		LowestSuccessRate,
		HighestProblemCount,
		HighestProblemRate
	}
}
