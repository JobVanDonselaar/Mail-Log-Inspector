using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed partial class MailLogInspectorStore
{	public MailLogInspectorImportResult SaveImport(string sourcePath, string sourceHash, string? archivePath, IEnumerable<SmtpLogEntry> entries, int errorCount, bool rebuildAnalysis = true, CancellationToken cancellationToken = default)
	{
		SqliteConnection val = OpenConnection();
		try
		{
			MailLogInspectorSchema.Ensure(val);
			SqliteTransaction val2 = val.BeginTransaction();
			try
			{
				long? num = TryReadImportId(val, val2, sourceHash);
				if (num.HasValue)
				{
					((DbTransaction)(object)val2).Rollback();
					MailLogInspectorImportedFile mailLogInspectorImportedFile = ReadImport(val, num.Value);
					return new MailLogInspectorImportResult(AlreadyImported: true, mailLogInspectorImportedFile.ImportId, mailLogInspectorImportedFile.SourcePath, mailLogInspectorImportedFile.RowCount, 0, errorCount, mailLogInspectorImportedFile.ReportStart, mailLogInspectorImportedFile.ReportEnd, mailLogInspectorImportedFile.ArchivePath);
				}
				DateTime utcNow = DateTime.UtcNow;
				int num2 = 0;
				int num3 = 0;
				int deliveredCount = 0;
				int bounceCount = 0;
				int underwayCount = 0;
				Dictionary<int, int> reasonCounts = new Dictionary<int, int>();
				DateTime? dateTime = null;
				DateTime? dateTime2 = null;
				Dictionary<string, long> domainIds = new Dictionary<string, long>(StringComparer.Ordinal);
				Dictionary<string, (long, long?)> addressReferences = new Dictionary<string, (long, long?)>(StringComparer.Ordinal);
				long importId = InsertImport(val, val2, sourcePath, sourceHash, archivePath, 0, utcNow, null, null);
				using SqliteCommand upsertMailItemCommand = CreateUpsertMailItemCommand(val, val2);
				using SqliteCommand insertDomainCommand = CreatePreparedCommand(val, val2, "INSERT OR IGNORE INTO mail_domains (domain_name) VALUES ($domainName);", ("$domainName", SqliteType.Text));
				using SqliteCommand selectDomainCommand = CreatePreparedCommand(val, val2, "SELECT domain_id FROM mail_domains WHERE domain_name = $domainName LIMIT 1;", ("$domainName", SqliteType.Text));
				using SqliteCommand insertAddressCommand = CreatePreparedCommand(val, val2, "INSERT OR IGNORE INTO mail_addresses (local_part, domain_id) VALUES ($localPart, $domainId);", ("$localPart", SqliteType.Text), ("$domainId", SqliteType.Integer));
				using SqliteCommand selectAddressCommand = CreatePreparedCommand(val, val2, "SELECT address_id FROM mail_addresses WHERE local_part = $localPart AND ((domain_id IS NULL AND $domainId IS NULL) OR domain_id = $domainId) LIMIT 1;", ("$localPart", SqliteType.Text), ("$domainId", SqliteType.Integer));
				foreach (SmtpLogEntry entry in entries)
				{
					num2++;
					if (num2 % 512 == 0)
					{
						cancellationToken.ThrowIfCancellationRequested();
					}
					if (entry.AcceptedAt.HasValue)
					{
						dateTime = ((!dateTime.HasValue || entry.AcceptedAt.Value < dateTime.Value) ? new DateTime?(entry.AcceptedAt.Value) : dateTime);
						dateTime2 = ((!dateTime2.HasValue || entry.AcceptedAt.Value > dateTime2.Value) ? new DateTime?(entry.AcceptedAt.Value) : dateTime2);
					}
					(long, long?) senderReference = ResolveAddressReference(insertAddressCommand, selectAddressCommand, insertDomainCommand, selectDomainCommand, addressReferences, domainIds, entry.MailFrom, entry.MailFromDomain);
					(long, long?) recipientReference = ResolveAddressReference(insertAddressCommand, selectAddressCommand, insertDomainCommand, selectDomainCommand, addressReferences, domainIds, entry.Recipient, entry.RecipientDomain);
					string importedStatus = ResolveCompactStatus(entry.Status);
					switch (ToStatusCode(importedStatus))
					{
					case 1:
						deliveredCount++;
						break;
					case 3:
						bounceCount++;
						int reason = (int)MailLogInspectorAttemptMeaning.ClassifyReason(entry.Status, entry.ResponseCode, entry.ResponseMessage, entry.BounceClass);
						reasonCounts[reason] = reasonCounts.GetValueOrDefault(reason) + 1;
						break;
					default:
						underwayCount++;
						break;
					}
					UpsertMailItem(upsertMailItemCommand, importId, entry, senderReference, recipientReference);
					num3++;
				}
				UpdateImport(val, val2, importId, num2, dateTime, dateTime2, deliveredCount, bounceCount, underwayCount);
				PersistImportReasonCounts(val, val2, importId, reasonCounts);
				if (rebuildAnalysis)
				{
					RebuildAnalysisTables(val, val2, cancellationToken);
				}
				else
				{
					SetSenderDomainAnalyticsVersion(val, val2, 0);
				}
				cancellationToken.ThrowIfCancellationRequested();
				((DbTransaction)(object)val2).Commit();
				return new MailLogInspectorImportResult(AlreadyImported: false, importId, sourcePath, num2, num3, errorCount, dateTime, dateTime2, archivePath);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public void RebuildAnalysisData()
	{
		using SqliteConnection connection = OpenConnection();
		MailLogInspectorSchema.Ensure(connection);
		RebuildAnalysisTables(connection);
	}
	public MailLogInspectorImportedFile? TryReadImportBySourceHash(string sourceHash)
	{
		SqliteConnection val = OpenConnection();
		try
		{
			MailLogInspectorSchema.Ensure(val);
			long? num = TryReadImportId(val, null, sourceHash);
			return (!num.HasValue) ? null : ReadImport(val, num.Value);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static MailLogInspectorImportedFile ReadImport(SqliteConnection connection, long importId)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			((DbCommand)(object)val).CommandText = "SELECT import_id,\n       source_path,\n       source_file_name,\n       source_hash,\n       imported_at,\n       report_start,\n       report_end,\n       row_count,\n       archive_path,\n       delivered_count,\n       bounce_count,\n       underway_count\nFROM imports\nWHERE import_id = $importId;";
			val.Parameters.AddWithValue("$importId", (object)importId);
			SqliteDataReader val2 = val.ExecuteReader();
			try
			{
				if (!((DbDataReader)(object)val2).Read())
				{
					throw new InvalidOperationException($"Import #{importId} not found.");
				}
				return new MailLogInspectorImportedFile(((DbDataReader)(object)val2).GetInt64(0), ((DbDataReader)(object)val2).GetString(1), ((DbDataReader)(object)val2).GetString(2), ((DbDataReader)(object)val2).GetString(3), ((DbDataReader)(object)val2).GetDateTime(4), ReadDateTime(val2, 5), ReadDateTime(val2, 6), ((DbDataReader)(object)val2).GetInt32(7), ((DbDataReader)(object)val2).IsDBNull(8) ? null : ((DbDataReader)(object)val2).GetString(8), ((DbDataReader)(object)val2).GetInt32(9), ((DbDataReader)(object)val2).GetInt32(10), ((DbDataReader)(object)val2).GetInt32(11));
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static long? TryReadImportId(SqliteConnection connection, SqliteTransaction? transaction, string sourceHash)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			val.Transaction = transaction;
			((DbCommand)(object)val).CommandText = "SELECT import_id FROM imports WHERE source_hash = $sourceHash LIMIT 1;";
			val.Parameters.AddWithValue("$sourceHash", (object)sourceHash);
			object? obj = ((DbCommand)(object)val).ExecuteScalar();
			return obj is null ? null : Convert.ToInt64(obj);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static long InsertImport(SqliteConnection connection, SqliteTransaction transaction, string sourcePath, string sourceHash, string? archivePath, int rowCount, DateTime importedAt, DateTime? reportStart, DateTime? reportEnd)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			val.Transaction = transaction;
			((DbCommand)(object)val).CommandText = "INSERT INTO imports (\n    source_path,\n    source_file_name,\n    source_hash,\n    imported_at,\n    report_start,\n    report_end,\n    row_count,\n    delivered_count,\n    bounce_count,\n    underway_count,\n    archive_path)\nVALUES (\n    $sourcePath,\n    $sourceFileName,\n    $sourceHash,\n    $importedAt,\n    $reportStart,\n    $reportEnd,\n    $rowCount,\n    0,\n    0,\n    0,\n    $archivePath);\n\nSELECT last_insert_rowid();";
			val.Parameters.AddWithValue("$sourcePath", (object)sourcePath);
			val.Parameters.AddWithValue("$sourceFileName", (object)Path.GetFileName(sourcePath));
			val.Parameters.AddWithValue("$sourceHash", (object)sourceHash);
			val.Parameters.AddWithValue("$importedAt", (object)importedAt);
			val.Parameters.AddWithValue("$reportStart", DbValue(reportStart));
			val.Parameters.AddWithValue("$reportEnd", DbValue(reportEnd));
			val.Parameters.AddWithValue("$rowCount", (object)rowCount);
			val.Parameters.AddWithValue("$archivePath", DbValue(archivePath));
			return (long)(((DbCommand)(object)val).ExecuteScalar() ?? ((object)0L));
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static void UpdateImport(
		SqliteConnection connection,
		SqliteTransaction transaction,
		long importId,
		int rowCount,
		DateTime? reportStart,
		DateTime? reportEnd,
		int deliveredCount,
		int bounceCount,
		int underwayCount)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = """
			UPDATE imports
			SET row_count = $rowCount,
			    report_start = $reportStart,
			    report_end = $reportEnd,
			    delivered_count = $deliveredCount,
			    bounce_count = $bounceCount,
			    underway_count = $underwayCount
			WHERE import_id = $importId;
			""";
		command.Parameters.AddWithValue("$importId", importId);
		command.Parameters.AddWithValue("$rowCount", rowCount);
		command.Parameters.AddWithValue("$reportStart", DbValue(reportStart));
		command.Parameters.AddWithValue("$reportEnd", DbValue(reportEnd));
		command.Parameters.AddWithValue("$deliveredCount", deliveredCount);
		command.Parameters.AddWithValue("$bounceCount", bounceCount);
		command.Parameters.AddWithValue("$underwayCount", underwayCount);
		command.ExecuteNonQuery();
	}

	private static void PersistImportReasonCounts(SqliteConnection connection, SqliteTransaction transaction, long importId, IReadOnlyDictionary<int, int> reasonCounts)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "INSERT INTO import_reason_counts (import_id, reason_code, total) VALUES ($importId, $reasonCode, $total);";
		SqliteParameter importParameter = command.Parameters.Add("$importId", SqliteType.Integer);
		SqliteParameter reasonParameter = command.Parameters.Add("$reasonCode", SqliteType.Integer);
		SqliteParameter totalParameter = command.Parameters.Add("$total", SqliteType.Integer);
		importParameter.Value = importId;
		foreach (KeyValuePair<int, int> reasonCount in reasonCounts)
		{
			reasonParameter.Value = reasonCount.Key;
			totalParameter.Value = reasonCount.Value;
			command.ExecuteNonQuery();
		}
	}
	private static SqliteCommand CreateUpsertMailItemCommand(SqliteConnection connection, SqliteTransaction transaction)
	{
		SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = """
			INSERT INTO mail_items (
			    tracking_key, recipient_address_id, recipient_domain_id, sender_address_id, sender_domain_id,
			    accepted_at, status, last_seen_at, duration_seconds, response_code, reason_code, last_import_id)
			VALUES (
			    $trackingKey, $recipientAddressId, $recipientDomainId, $senderAddressId, $senderDomainId,
			    $acceptedAt, $status, $lastSeenAt, $durationSeconds, $responseCode, $reasonCode, $lastImportId)
			ON CONFLICT(tracking_key, recipient_address_id) DO UPDATE SET
			    sender_address_id = excluded.sender_address_id,
			    recipient_domain_id = COALESCE(excluded.recipient_domain_id, mail_items.recipient_domain_id),
			    sender_domain_id = COALESCE(excluded.sender_domain_id, mail_items.sender_domain_id),
			    accepted_at = COALESCE(MIN(mail_items.accepted_at, excluded.accepted_at), mail_items.accepted_at, excluded.accepted_at),
			    status = CASE WHEN excluded.status IN (1, 3) THEN excluded.status ELSE mail_items.status END,
			    last_seen_at = MAX(mail_items.last_seen_at, excluded.last_seen_at),
			    duration_seconds = COALESCE(excluded.duration_seconds, mail_items.duration_seconds),
			    response_code = COALESCE(excluded.response_code, mail_items.response_code),
			    reason_code = CASE WHEN excluded.reason_code <> 8 THEN excluded.reason_code ELSE mail_items.reason_code END,
			    last_import_id = excluded.last_import_id;
			""";
		command.Parameters.Add("$trackingKey", SqliteType.Blob);
		command.Parameters.Add("$recipientAddressId", SqliteType.Integer);
		command.Parameters.Add("$recipientDomainId", SqliteType.Integer);
		command.Parameters.Add("$senderAddressId", SqliteType.Integer);
		command.Parameters.Add("$senderDomainId", SqliteType.Integer);
		command.Parameters.Add("$acceptedAt", SqliteType.Integer);
		command.Parameters.Add("$status", SqliteType.Integer);
		command.Parameters.Add("$lastSeenAt", SqliteType.Integer);
		command.Parameters.Add("$durationSeconds", SqliteType.Integer);
		command.Parameters.Add("$responseCode", SqliteType.Integer);
		command.Parameters.Add("$reasonCode", SqliteType.Integer);
		command.Parameters.Add("$lastImportId", SqliteType.Integer);
		command.Prepare();
		return command;
	}

	private static void UpsertMailItem(SqliteCommand command, long importId, SmtpLogEntry entry, (long AddressId, long? DomainId) senderReference, (long AddressId, long? DomainId) recipientReference)
	{
		DateTime? acceptedAt = entry.AcceptedAt;
		string status = ResolveCompactStatus(entry.Status);
		DateTime lastSeenAt = ResolveLastSeenAt(entry);
		int? durationSeconds = acceptedAt.HasValue && IsFinalStatus(status)
			? (int)Math.Max(0.0, (lastSeenAt - acceptedAt.Value).TotalSeconds)
			: null;
		command.Parameters["$trackingKey"].Value = BuildTrackingKey(entry.TrackingId, entry.Recipient);
		command.Parameters["$recipientAddressId"].Value = recipientReference.AddressId;
		command.Parameters["$recipientDomainId"].Value = DbValue(recipientReference.DomainId);
		command.Parameters["$senderAddressId"].Value = senderReference.AddressId;
		command.Parameters["$senderDomainId"].Value = DbValue(senderReference.DomainId);
		command.Parameters["$acceptedAt"].Value = DbValueTicks(acceptedAt);
		command.Parameters["$status"].Value = ToStatusCode(status);
		command.Parameters["$lastSeenAt"].Value = ToStoredTicks(lastSeenAt);
		command.Parameters["$durationSeconds"].Value = DbValue(durationSeconds);
		command.Parameters["$responseCode"].Value = DbValue(NormalizeResponseCode(entry.ResponseCode));
		command.Parameters["$reasonCode"].Value = (int)MailLogInspectorAttemptMeaning.ClassifyReason(entry.Status, entry.ResponseCode, entry.ResponseMessage, entry.BounceClass);
		command.Parameters["$lastImportId"].Value = importId;
		command.ExecuteNonQuery();
	}
	private static string ResolveCompactStatus(string rawStatus)
	{
		if (!IsDelivered(rawStatus))
		{
			if (!IsBounce(rawStatus))
			{
				return "onderweg";
			}
			return "bounce";
		}
		return "afgeleverd";
	}

	private static bool IsDelivered(string rawStatus)
	{
		return string.Equals(rawStatus?.Trim(), "D", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsBounce(string rawStatus)
	{
		return string.Equals(rawStatus?.Trim(), "B", StringComparison.OrdinalIgnoreCase);
	}

	private static (long AddressId, long? DomainId) ResolveAddressReference(
		SqliteCommand insertAddressCommand,
		SqliteCommand selectAddressCommand,
		SqliteCommand insertDomainCommand,
		SqliteCommand selectDomainCommand,
		IDictionary<string, (long AddressId, long? DomainId)> addressReferences,
		IDictionary<string, long> domainIds,
		string email,
		string domain)
	{
		string normalizedEmail = email.Trim().ToLowerInvariant();
		if (addressReferences.TryGetValue(normalizedEmail, out (long, long?) existing))
		{
			return existing;
		}

		string normalizedDomain = domain.Trim().ToLowerInvariant();
		string localPart = ExtractLocalPart(normalizedEmail);
		long? domainId = null;
		if (!string.IsNullOrWhiteSpace(normalizedDomain))
		{
			if (!domainIds.TryGetValue(normalizedDomain, out long resolvedDomainId))
			{
				resolvedDomainId = EnsureDomainId(insertDomainCommand, selectDomainCommand, normalizedDomain);
				domainIds[normalizedDomain] = resolvedDomainId;
			}
			domainId = resolvedDomainId;
		}

		insertAddressCommand.Parameters["$localPart"].Value = localPart;
		insertAddressCommand.Parameters["$domainId"].Value = DbValue(domainId);
		insertAddressCommand.ExecuteNonQuery();
		selectAddressCommand.Parameters["$localPart"].Value = localPart;
		selectAddressCommand.Parameters["$domainId"].Value = DbValue(domainId);
		long addressId = Convert.ToInt64(selectAddressCommand.ExecuteScalar() ?? 0L);
		return addressReferences[normalizedEmail] = (addressId, domainId);
	}

	private static long EnsureDomainId(SqliteCommand insertDomainCommand, SqliteCommand selectDomainCommand, string domain)
	{
		insertDomainCommand.Parameters["$domainName"].Value = domain;
		insertDomainCommand.ExecuteNonQuery();
		selectDomainCommand.Parameters["$domainName"].Value = domain;
		return Convert.ToInt64(selectDomainCommand.ExecuteScalar() ?? 0L);
	}

	private static SqliteCommand CreatePreparedCommand(
		SqliteConnection connection,
		SqliteTransaction transaction,
		string commandText,
		params (string Name, SqliteType Type)[] parameters)
	{
		SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = commandText;
		foreach ((string name, SqliteType type) in parameters)
		{
			command.Parameters.Add(name, type);
		}
		command.Prepare();
		return command;
	}
	private static int? NormalizeResponseCode(string? value) => int.TryParse(value?.Trim(), out int result) ? result : null;

	private static bool IsFinalStatus(string compactStatus)
	{
		if (compactStatus == "afgeleverd" || compactStatus == "bounce")
		{
			return true;
		}
		return false;
	}

	private static DateTime ResolveLastSeenAt(SmtpLogEntry entry)
	{
		return entry.DeliveredAt ?? entry.AcceptedAt ?? DateTime.UtcNow;
	}

	private static string ExtractLocalPart(string email)
	{
		int num = email.LastIndexOf('@');
		if (num <= 0)
		{
			return email;
		}
		return email.Substring(0, num);
	}

	internal static DateTime? ReadStoredDateTime(SqliteDataReader reader, int ordinal)
	{
		if (((DbDataReader)(object)reader).IsDBNull(ordinal))
		{
			return null;
		}
		return FromStoredTicks(((DbDataReader)(object)reader).GetInt64(ordinal));
	}

	internal static long ToStoredTicks(DateTime value) => value.Ticks;

	internal static DateTime FromStoredTicks(long value) => new DateTime(value);

	internal static int ToStatusCode(string? compactStatus)
	{
		return compactStatus?.Trim().ToLowerInvariant() switch
		{
			"afgeleverd" => 1,
			"onderweg" => 2,
			"bounce" => 3,
			_ => 2
		};
	}

	internal static string FromStatusCode(int statusCode)
	{
		return statusCode switch
		{
			1 => "afgeleverd",
			3 => "bounce",
			_ => "onderweg"
		};
	}

	internal static string DescribeReason(int reasonCode)
	{
		return MailLogInspectorAttemptMeaning.DescribeReason((MailLogInspectorReasonCode)reasonCode);
	}

	private static byte[] BuildTrackingKey(string? trackingId, string? recipient)
	{
		string normalizedTrackingId = (trackingId ?? string.Empty).Trim();
		if (Guid.TryParse(normalizedTrackingId, out Guid guid))
		{
			return guid.ToByteArray();
		}

		string normalizedRecipient = (recipient ?? string.Empty).Trim().ToLowerInvariant();
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedTrackingId.ToLowerInvariant() + "|" + normalizedRecipient));
		byte[] key = new byte[16];
		Array.Copy(hash, key, key.Length);
		return key;
	}

	private void RebuildAnalysisTables(SqliteConnection connection)
	{
		using SqliteTransaction transaction = connection.BeginTransaction();
		RebuildAnalysisTables(connection, transaction, CancellationToken.None);
		transaction.Commit();
		using SqliteCommand optimizeCommand = connection.CreateCommand();
		optimizeCommand.CommandText = "PRAGMA optimize;";
		optimizeCommand.ExecuteNonQuery();
	}

	private void RebuildAnalysisTables(
		SqliteConnection connection,
		SqliteTransaction transaction,
		CancellationToken cancellationToken)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		((DbCommand)(object)command).CommandText = "DELETE FROM analysis_daily_status;\nDELETE FROM analysis_daily_sender_domain;\nDELETE FROM analysis_daily_sender_reason;\nDELETE FROM analysis_daily_recipient_domain;\nDELETE FROM analysis_daily_reason;\n\nINSERT INTO analysis_daily_status (\n    day_key, status, total, duration_metrics_version, duration_count,\n    duration_sum_seconds, duration_missing_count, within_60_count,\n    within_300_count, within_900_count, within_3600_count)\nSELECT accepted_at / 864000000000,\n       status,\n       COUNT(*),\n       1,\n       COUNT(duration_seconds),\n       COALESCE(SUM(duration_seconds), 0),\n       SUM(CASE WHEN duration_seconds IS NULL THEN 1 ELSE 0 END),\n       SUM(CASE WHEN duration_seconds IS NOT NULL AND duration_seconds <= 60 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN duration_seconds IS NOT NULL AND duration_seconds <= 300 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN duration_seconds IS NOT NULL AND duration_seconds <= 900 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN duration_seconds IS NOT NULL AND duration_seconds <= 3600 THEN 1 ELSE 0 END)\nFROM mail_items\nWHERE accepted_at IS NOT NULL\nGROUP BY accepted_at / 864000000000, status;\n\nINSERT INTO analysis_daily_sender_domain (\n    day_key, domain_id, total, delivered, underway, bounce,\n    duration_metrics_version, duration_count, duration_sum_seconds,\n    duration_missing_count, within_60_count, within_300_count,\n    within_900_count, within_3600_count)\nSELECT accepted_at / 864000000000,\n       COALESCE(sender_domain_id, 0),\n       COUNT(*),\n       SUM(CASE WHEN status = 1 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 2 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 3 THEN 1 ELSE 0 END),\n       1,\n       SUM(CASE WHEN status = 1 AND duration_seconds IS NOT NULL THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 1 THEN COALESCE(duration_seconds, 0) ELSE 0 END),\n       SUM(CASE WHEN status = 1 AND duration_seconds IS NULL THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 1 AND duration_seconds IS NOT NULL AND duration_seconds <= 60 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 1 AND duration_seconds IS NOT NULL AND duration_seconds <= 300 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 1 AND duration_seconds IS NOT NULL AND duration_seconds <= 900 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 1 AND duration_seconds IS NOT NULL AND duration_seconds <= 3600 THEN 1 ELSE 0 END)\nFROM mail_items\nWHERE accepted_at IS NOT NULL\nGROUP BY accepted_at / 864000000000, COALESCE(sender_domain_id, 0);\n\nINSERT INTO analysis_daily_sender_reason (day_key, domain_id, reason_code, total)\nSELECT accepted_at / 864000000000,\n       COALESCE(sender_domain_id, 0),\n       reason_code,\n       COUNT(*)\nFROM mail_items\nWHERE accepted_at IS NOT NULL\n  AND status = 3\nGROUP BY accepted_at / 864000000000, COALESCE(sender_domain_id, 0), reason_code;\n\nINSERT INTO analysis_daily_recipient_domain (day_key, domain_id, total, delivered, underway, bounce)\nSELECT accepted_at / 864000000000,\n       COALESCE(recipient_domain_id, 0),\n       COUNT(*),\n       SUM(CASE WHEN status = 1 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 2 THEN 1 ELSE 0 END),\n       SUM(CASE WHEN status = 3 THEN 1 ELSE 0 END)\nFROM mail_items\nWHERE accepted_at IS NOT NULL\nGROUP BY accepted_at / 864000000000, COALESCE(recipient_domain_id, 0);\n\nINSERT INTO analysis_daily_reason (day_key, reason_code, response_code, total)\nSELECT accepted_at / 864000000000,\n       reason_code,\n       COALESCE(response_code, 0),\n       COUNT(*)\nFROM mail_items\nWHERE accepted_at IS NOT NULL\n  AND status = 3\nGROUP BY accepted_at / 864000000000, reason_code, COALESCE(response_code, 0);";
		using CancellationTokenRegistration registration =
			cancellationToken.Register(command.Cancel);
		cancellationToken.ThrowIfCancellationRequested();
		((DbCommand)(object)command).ExecuteNonQuery();
		cancellationToken.ThrowIfCancellationRequested();
		using SqliteCommand responseCommand = connection.CreateCommand();
		responseCommand.Transaction = transaction;
		responseCommand.CommandText = """
			DELETE FROM analysis_daily_response;

			INSERT INTO analysis_daily_response (day_key, response_code, total)
			SELECT accepted_at / 864000000000,
			       COALESCE(response_code, 0),
			       COUNT(*)
			FROM mail_items
			WHERE accepted_at IS NOT NULL
			GROUP BY accepted_at / 864000000000, COALESCE(response_code, 0);
			""";
		using CancellationTokenRegistration responseRegistration =
			cancellationToken.Register(responseCommand.Cancel);
		responseCommand.ExecuteNonQuery();
		cancellationToken.ThrowIfCancellationRequested();
		SetSenderDomainAnalyticsVersion(connection, transaction, CurrentSenderDomainAnalyticsVersion);
	}


	private static object DbValue(DateTime? value) => value.HasValue ? value.Value : DBNull.Value;

	private static object DbValueTicks(DateTime? value) => value.HasValue ? value.Value.Ticks : DBNull.Value;

	private static object DbValue(long? value) => value.HasValue ? value.Value : DBNull.Value;

	private static object DbValue(int? value) => value.HasValue ? value.Value : DBNull.Value;

	private static object DbValue(string? value) => value is null ? DBNull.Value : value;
}
