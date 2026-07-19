using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using Microsoft.Data.Sqlite;
using MailLogInspector.Core;

namespace MailLogInspector.Storage;

public sealed class MailLogInspectorSearchService
{
	private readonly MailLogInspectorStore _store;

	public MailLogInspectorSearchService(MailLogInspectorStore store)
	{
		_store = store;
	}

	public IReadOnlyList<MailLogInspectorSearchRow> Search(MailLogInspectorSearchCriteria criteria, int limit = 500, CancellationToken cancellationToken = default)
	{
		using SqliteConnection connection = _store.OpenConnection();
		using SqliteCommand? command = BuildSearchCommand(connection, criteria, limit);
		if (command is null)
		{
			return Array.Empty<MailLogInspectorSearchRow>();
		}

		cancellationToken.ThrowIfCancellationRequested();
		command.CommandTimeout = 30;
		using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(command.Cancel);
		using SqliteDataReader reader = command.ExecuteReader();
		List<MailLogInspectorSearchRow> rows = new();
		while (((DbDataReader)(object)reader).Read())
		{
			rows.Add(new MailLogInspectorSearchRow(
				MailLogInspectorStore.ReadStoredDateTime(reader, 0),
				ComposeEmail(((DbDataReader)(object)reader).GetString(1), ((DbDataReader)(object)reader).IsDBNull(2) ? null : ((DbDataReader)(object)reader).GetString(2)),
				ComposeEmail(((DbDataReader)(object)reader).GetString(3), ((DbDataReader)(object)reader).IsDBNull(4) ? null : ((DbDataReader)(object)reader).GetString(4)),
				string.Empty,
				MailLogInspectorStore.FromStatusCode(((DbDataReader)(object)reader).GetInt32(5)),
				((DbDataReader)(object)reader).IsDBNull(6) ? null : ((DbDataReader)(object)reader).GetInt32(6),
				(MailLogInspectorReasonCode)((DbDataReader)(object)reader).GetInt32(7),
				MailLogInspectorStore.DescribeReason(((DbDataReader)(object)reader).GetInt32(7)),
				MailLogInspectorStore.FromStoredTicks(((DbDataReader)(object)reader).GetInt64(8)),
				MailLogInspectorStore.FromStoredTicks(((DbDataReader)(object)reader).GetInt64(9)),
				((DbDataReader)(object)reader).GetString(10)));
		}

		return rows;
	}

	public MailLogInspectorSearchSummary ReadSummary(MailLogInspectorSearchCriteria criteria, CancellationToken cancellationToken = default)
	{
		using SqliteConnection connection = _store.OpenConnection();
		FilterScope scope = ResolveSearchScope(connection, criteria);
		if (scope.IsUnsatisfiable)
		{
			return new MailLogInspectorSearchSummary(0, 0, 0, 0);
		}

		if (TryReadAggregateSummary(connection, criteria, scope, cancellationToken, out MailLogInspectorSearchSummary? aggregateSummary))
		{
			return aggregateSummary;
		}

		using SqliteCommand command = BuildSummaryCommandForScope(connection, criteria, scope);
		cancellationToken.ThrowIfCancellationRequested();
		command.CommandTimeout = 30;
		using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(command.Cancel);
		using SqliteDataReader reader = command.ExecuteReader();
		reader.Read();
		return new MailLogInspectorSearchSummary(ReadInt32(reader, 0), ReadInt32(reader, 1), ReadInt32(reader, 2), ReadInt32(reader, 3));
	}
	private static SqliteCommand? BuildSearchCommand(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, int limit)
	{
		FilterScope scope = ResolveSearchScope(connection, criteria);
		if (scope.IsUnsatisfiable)
		{
			return null;
		}

		SqliteCommand command = connection.CreateCommand();
		StringBuilder sql = new StringBuilder();
		sql.Append("SELECT item.accepted_at,\n")
			.Append("       sender.local_part,\n")
			.Append("       sender_domain.domain_name,\n")
			.Append("       recipient.local_part,\n")
			.Append("       recipient_domain.domain_name,\n")
			.Append("       item.status,\n")
			.Append("       item.duration_seconds,\n")
			.Append("       item.reason_code,\n")
			.Append("       COALESCE(item.accepted_at, item.last_seen_at),\n")
			.Append("       item.last_seen_at,\n")
			.Append("       COALESCE(imports.source_file_name, '')\n")
			.Append("FROM mail_items AS item\n")
			.Append("JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id\n")
			.Append("LEFT JOIN mail_domains AS sender_domain ON sender_domain.domain_id = item.sender_domain_id\n")
			.Append("JOIN mail_addresses AS recipient ON recipient.address_id = item.recipient_address_id\n")
			.Append("LEFT JOIN mail_domains AS recipient_domain ON recipient_domain.domain_id = item.recipient_domain_id\n")
			.Append("LEFT JOIN imports ON imports.import_id = item.last_import_id\n")
			.Append("WHERE 1 = 1\n");
		AppendCommonFilters(command, sql, scope, criteria);
		int? statusCode = StatusToCode(criteria.Status);
		if (statusCode.HasValue)
		{
			sql.Append("  AND item.status = $status\n");
			command.Parameters.AddWithValue("$status", statusCode.Value);
		}

		sql.Append("ORDER BY item.accepted_at DESC, item.last_seen_at DESC\n")
			.Append("LIMIT $limit;");
		command.Parameters.AddWithValue("$limit", limit);
		((DbCommand)(object)command).CommandText = sql.ToString();
		return command;
	}

	private static SqliteCommand BuildSummaryCommand(SqliteConnection connection, MailLogInspectorSearchCriteria criteria)
	{
		FilterScope scope = ResolveSearchScope(connection, criteria);
		return BuildSummaryCommandForScope(connection, criteria, scope);
	}
	private static SqliteCommand BuildSummaryCommandForScope(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, FilterScope scope)
	{
		SqliteCommand command = connection.CreateCommand();
		StringBuilder sql = new StringBuilder();
		sql.Append("SELECT COUNT(*),\n")
			.Append("       SUM(CASE WHEN item.status = $deliveredStatus THEN 1 ELSE 0 END),\n")
			.Append("       SUM(CASE WHEN item.status = $underwayStatus THEN 1 ELSE 0 END),\n")
			.Append("       SUM(CASE WHEN item.status = $bounceStatus THEN 1 ELSE 0 END)\n")
			.Append("FROM mail_items AS item\n");
		if (scope.RequiresSenderLocalJoin)
		{
			sql.Append("JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id\n");
		}

		if (scope.RequiresRecipientLocalJoin)
		{
			sql.Append("JOIN mail_addresses AS recipient ON recipient.address_id = item.recipient_address_id\n");
		}

		sql.Append("WHERE 1 = 1\n");
		AppendCommonFilters(command, sql, scope, criteria);
		int? statusCode = StatusToCode(criteria.Status);
		if (statusCode.HasValue)
		{
			sql.Append("  AND item.status = $status\n");
			command.Parameters.AddWithValue("$status", statusCode.Value);
		}

		command.Parameters.AddWithValue("$deliveredStatus", 1);
		command.Parameters.AddWithValue("$underwayStatus", 2);
		command.Parameters.AddWithValue("$bounceStatus", 3);
		command.CommandText = sql.ToString();
		return command;
	}

	private static bool TryReadAggregateSummary(
		SqliteConnection connection,
		MailLogInspectorSearchCriteria criteria,
		FilterScope scope,
		CancellationToken cancellationToken,
		out MailLogInspectorSearchSummary summary)
	{
		summary = new MailLogInspectorSearchSummary(0, 0, 0, 0);
		if (criteria.FromInclusive.TimeOfDay != TimeSpan.Zero ||
			criteria.ThroughInclusive.TimeOfDay < new TimeSpan(23, 59, 0) ||
			scope.SenderParts.LocalPart is not null ||
			scope.RecipientParts.LocalPart is not null ||
			(scope.SenderDomainId.HasValue && scope.RecipientDomainId.HasValue))
		{
			return false;
		}

		using SqliteCommand command = connection.CreateCommand();
		command.CommandTimeout = 30;
		command.Parameters.AddWithValue("$fromDay", criteria.FromInclusive.Ticks / TimeSpan.TicksPerDay);
		command.Parameters.AddWithValue("$throughDay", criteria.ThroughInclusive.Ticks / TimeSpan.TicksPerDay);
		int? statusCode = StatusToCode(criteria.Status);
		bool domainScoped = scope.SenderDomainId.HasValue || scope.RecipientDomainId.HasValue;
		if (!domainScoped)
		{
			command.CommandText = """
				SELECT SUM(total),
				       SUM(CASE WHEN status = 1 THEN total ELSE 0 END),
				       SUM(CASE WHEN status = 2 THEN total ELSE 0 END),
				       SUM(CASE WHEN status = 3 THEN total ELSE 0 END)
				FROM analysis_daily_status
				WHERE day_key >= $fromDay AND day_key <= $throughDay
				  AND ($status IS NULL OR status = $status);
				""";
			command.Parameters.AddWithValue("$status", statusCode.HasValue ? statusCode.Value : DBNull.Value);
		}
		else
		{
			string tableName = scope.SenderDomainId.HasValue ? "analysis_daily_sender_domain" : "analysis_daily_recipient_domain";
			command.CommandText = $"SELECT SUM(total), SUM(delivered), SUM(underway), SUM(bounce) FROM {tableName} WHERE day_key >= $fromDay AND day_key <= $throughDay AND domain_id = $domainId;";
			command.Parameters.AddWithValue("$domainId", (scope.SenderDomainId ?? scope.RecipientDomainId)!.Value);
		}

		cancellationToken.ThrowIfCancellationRequested();
		using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(command.Cancel);
		using SqliteDataReader reader = command.ExecuteReader();
		reader.Read();
		int total = ReadInt32(reader, 0);
		int delivered = ReadInt32(reader, 1);
		int underway = ReadInt32(reader, 2);
		int bounce = ReadInt32(reader, 3);
		if (domainScoped && statusCode.HasValue)
		{
			total = statusCode.Value switch { 1 => delivered, 2 => underway, 3 => bounce, _ => 0 };
			delivered = statusCode.Value == 1 ? delivered : 0;
			underway = statusCode.Value == 2 ? underway : 0;
			bounce = statusCode.Value == 3 ? bounce : 0;
		}

		summary = new MailLogInspectorSearchSummary(total, delivered, underway, bounce);
		return true;
	}
	private static void AppendCommonFilters(SqliteCommand command, StringBuilder sql, FilterScope scope, MailLogInspectorSearchCriteria criteria)
	{
		sql.Append("  AND item.accepted_at >= $fromInclusive\n");
		command.Parameters.AddWithValue("$fromInclusive", criteria.FromInclusive.Ticks);
		sql.Append("  AND item.accepted_at <= $throughInclusive\n");
		command.Parameters.AddWithValue("$throughInclusive", criteria.ThroughInclusive.Ticks);

		if (scope.SenderAddressId.HasValue)
		{
			sql.Append("  AND item.sender_address_id = $senderAddressId\n");
			command.Parameters.AddWithValue("$senderAddressId", scope.SenderAddressId.Value);
		}
		else
		{
			if (scope.SenderParts.LocalPart is not null)
			{
				sql.Append("  AND sender.local_part = $senderLocal\n");
				command.Parameters.AddWithValue("$senderLocal", scope.SenderParts.LocalPart);
			}

			if (scope.SenderDomainId.HasValue)
			{
				sql.Append("  AND item.sender_domain_id = $senderDomainId\n");
				command.Parameters.AddWithValue("$senderDomainId", scope.SenderDomainId.Value);
			}
		}

		if (scope.RecipientAddressId.HasValue)
		{
			sql.Append("  AND item.recipient_address_id = $recipientAddressId\n");
			command.Parameters.AddWithValue("$recipientAddressId", scope.RecipientAddressId.Value);
		}
		else
		{
			if (scope.RecipientParts.LocalPart is not null)
			{
				sql.Append("  AND recipient.local_part = $recipientLocal\n");
				command.Parameters.AddWithValue("$recipientLocal", scope.RecipientParts.LocalPart);
			}

			if (scope.RecipientDomainId.HasValue)
			{
				sql.Append("  AND item.recipient_domain_id = $recipientDomainId\n");
				command.Parameters.AddWithValue("$recipientDomainId", scope.RecipientDomainId.Value);
			}
		}
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

	private static bool HasUnresolvedAddress(bool exactAddressRequested, long? resolvedAddressId)
	{
		return exactAddressRequested && !resolvedAddressId.HasValue;
	}

	private static long? ResolveDomainId(SqliteConnection connection, string? domain)
	{
		if (domain is null)
		{
			return null;
		}

		using SqliteCommand command = connection.CreateCommand();
		((DbCommand)(object)command).CommandText = "SELECT domain_id FROM mail_domains WHERE domain_name = $domain LIMIT 1;";
		command.Parameters.AddWithValue("$domain", domain);
		object? result = ((DbCommand)(object)command).ExecuteScalar();
		return result is null ? null : Convert.ToInt64(result);
	}

	private static long? ResolveAddressId(SqliteConnection connection, string? localPart, long? domainId)
	{
		if (localPart is null || !domainId.HasValue)
		{
			return null;
		}

		using SqliteCommand command = connection.CreateCommand();
		((DbCommand)(object)command).CommandText = "SELECT address_id FROM mail_addresses WHERE local_part = $localPart AND domain_id = $domainId LIMIT 1;";
		command.Parameters.AddWithValue("$localPart", localPart);
		command.Parameters.AddWithValue("$domainId", domainId.Value);
		object? result = ((DbCommand)(object)command).ExecuteScalar();
		return result is null ? null : Convert.ToInt64(result);
	}

	private static FilterScope ResolveSearchScope(SqliteConnection connection, MailLogInspectorSearchCriteria criteria)
	{
		var senderParts = SplitEmail(criteria.Sender);
		var recipientParts = SplitEmail(criteria.Recipient);
		string? senderDomain = senderParts.Domain ?? Normalize(criteria.SenderDomain);
		string? recipientDomain = recipientParts.Domain ?? Normalize(criteria.RecipientDomain);
		long? senderDomainId = ResolveDomainId(connection, senderDomain);
		long? recipientDomainId = ResolveDomainId(connection, recipientDomain);
		bool senderExactAddress = senderParts.LocalPart is not null && senderParts.Domain is not null;
		bool recipientExactAddress = recipientParts.LocalPart is not null && recipientParts.Domain is not null;
		long? senderAddressId = senderExactAddress ? ResolveAddressId(connection, senderParts.LocalPart, senderDomainId) : null;
		long? recipientAddressId = recipientExactAddress ? ResolveAddressId(connection, recipientParts.LocalPart, recipientDomainId) : null;
		return new FilterScope(
			senderParts,
			recipientParts,
			senderDomainId,
			recipientDomainId,
			senderAddressId,
			recipientAddressId,
			HasUnresolvedDomain(senderParts.Domain, criteria.SenderDomain, senderDomainId)
				|| HasUnresolvedDomain(recipientParts.Domain, criteria.RecipientDomain, recipientDomainId)
				|| HasUnresolvedAddress(senderExactAddress, senderAddressId)
				|| HasUnresolvedAddress(recipientExactAddress, recipientAddressId));
	}

	private static int ReadInt32(SqliteDataReader reader, int ordinal)
	{
		if (!((DbDataReader)(object)reader).IsDBNull(ordinal))
		{
			return Convert.ToInt32(((DbDataReader)(object)reader).GetValue(ordinal));
		}

		return 0;
	}

	private static int? StatusToCode(string? status)
	{
		string? normalized = Normalize(status);
		return normalized is null ? null : MailLogInspectorStore.ToStatusCode(normalized);
	}

	private static string ComposeEmail(string localPart, string? domain)
	{
		return string.IsNullOrWhiteSpace(domain) ? localPart : localPart + "@" + domain;
	}

	private readonly record struct FilterScope(
		(string? LocalPart, string? Domain) SenderParts,
		(string? LocalPart, string? Domain) RecipientParts,
		long? SenderDomainId,
		long? RecipientDomainId,
		long? SenderAddressId,
		long? RecipientAddressId,
		bool IsUnsatisfiable)
	{
		public bool RequiresSenderLocalJoin => !SenderAddressId.HasValue && SenderParts.LocalPart is not null;

		public bool RequiresRecipientLocalJoin => !RecipientAddressId.HasValue && RecipientParts.LocalPart is not null;
	}
}

