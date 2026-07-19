using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using MailLogInspector.Core;

namespace MailLogInspector.Storage;

internal static class MailLogInspectorSchema
{
	public static void Ensure(SqliteConnection connection)
	{
		EnsureImportsTable(connection);
		EnsureDomainLookupTable(connection);
		EnsureAddressLookupTable(connection);
		EnsureCompactMailItemsTable(connection, GetMailItemsState(connection));
		EnsureMailItemIndexes(connection);
		EnsureAnalysisTables(connection);
		EnsureAnalysisMetadataTable(connection);
		EnsureImportReasonCountsTable(connection);
	}

	private static void EnsureImportsTable(SqliteConnection connection)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			((DbCommand)(object)val).CommandText = "CREATE TABLE IF NOT EXISTS imports (\n    import_id INTEGER PRIMARY KEY AUTOINCREMENT,\n    source_path TEXT NOT NULL,\n    source_file_name TEXT NOT NULL,\n    source_hash TEXT NOT NULL,\n    imported_at TEXT NOT NULL,\n    report_start TEXT NULL,\n    report_end TEXT NULL,\n    row_count INTEGER NOT NULL,\n    delivered_count INTEGER NOT NULL DEFAULT 0,\n    bounce_count INTEGER NOT NULL DEFAULT 0,\n    underway_count INTEGER NOT NULL DEFAULT 0,\n    archive_path TEXT NULL\n);\n\nCREATE UNIQUE INDEX IF NOT EXISTS ux_mail_log_inspector_import_hash\n    ON imports(source_hash);";
			((DbCommand)(object)val).ExecuteNonQuery();
            AddColumnIfMissing(connection, "imports", "delivered_count", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "imports", "bounce_count", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "imports", "underway_count", "INTEGER NOT NULL DEFAULT 0");
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static void EnsureDomainLookupTable(SqliteConnection connection)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			((DbCommand)(object)val).CommandText = "CREATE TABLE IF NOT EXISTS mail_domains (\n    domain_id INTEGER PRIMARY KEY AUTOINCREMENT,\n    domain_name TEXT NOT NULL\n);\n\nCREATE UNIQUE INDEX IF NOT EXISTS ux_mail_domains_name\n    ON mail_domains(domain_name);";
			((DbCommand)(object)val).ExecuteNonQuery();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static void EnsureAddressLookupTable(SqliteConnection connection)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			((DbCommand)(object)val).CommandText = "CREATE TABLE IF NOT EXISTS mail_addresses (\n    address_id INTEGER PRIMARY KEY AUTOINCREMENT,\n    local_part TEXT NOT NULL,\n    domain_id INTEGER NULL\n);\n\nCREATE UNIQUE INDEX IF NOT EXISTS ux_mail_addresses_local_domain\n    ON mail_addresses(local_part, domain_id);\nDROP INDEX IF EXISTS ix_mail_addresses_domain;";
			((DbCommand)(object)val).ExecuteNonQuery();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static MailLogInspectorDatabaseState GetMailItemsState(SqliteConnection connection)
	{
		if (!HasTable(connection, "mail_items"))
		{
			return MailLogInspectorDatabaseState.MissingOrEmpty;
		}
		if (!MailItemsNeedsRebuild(connection))
		{
			return MailLogInspectorDatabaseState.Current;
		}
		return MailLogInspectorDatabaseState.RebuildRequired;
	}

	private static void EnsureCompactMailItemsTable(SqliteConnection connection, MailLogInspectorDatabaseState state)
	{
		switch (state)
		{
		case MailLogInspectorDatabaseState.Current:
			return;
		case MailLogInspectorDatabaseState.RebuildRequired:
			throw new InvalidOperationException("Deze database-indeling is verouderd. De applicatie bouwt de database opnieuw op vanuit de bronbestanden.");
		}

		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			CREATE TABLE IF NOT EXISTS mail_items (
			    mail_item_id INTEGER PRIMARY KEY,
			    tracking_key BLOB NOT NULL,
			    recipient_address_id INTEGER NOT NULL,
			    recipient_domain_id INTEGER NULL,
			    sender_address_id INTEGER NOT NULL,
			    sender_domain_id INTEGER NULL,
			    accepted_at INTEGER NULL,
			    status INTEGER NOT NULL,
			    last_seen_at INTEGER NOT NULL,
			    duration_seconds INTEGER NULL,
			    response_code INTEGER NULL,
			    reason_code INTEGER NOT NULL,
			    last_import_id INTEGER NOT NULL,
			    UNIQUE (tracking_key, recipient_address_id)
			);
			""";
		command.ExecuteNonQuery();
	}
	private static void EnsureAnalysisTables(SqliteConnection connection)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			CREATE TABLE IF NOT EXISTS analysis_daily_status (
			    day_key INTEGER NOT NULL,
			    status INTEGER NOT NULL,
			    total INTEGER NOT NULL,
			    duration_metrics_version INTEGER NOT NULL DEFAULT 0,
			    duration_count INTEGER NOT NULL DEFAULT 0,
			    duration_sum_seconds INTEGER NOT NULL DEFAULT 0,
			    duration_missing_count INTEGER NOT NULL DEFAULT 0,
			    within_60_count INTEGER NOT NULL DEFAULT 0,
			    within_300_count INTEGER NOT NULL DEFAULT 0,
			    within_900_count INTEGER NOT NULL DEFAULT 0,
			    within_3600_count INTEGER NOT NULL DEFAULT 0,
			    PRIMARY KEY(day_key, status)
			) WITHOUT ROWID;

			CREATE TABLE IF NOT EXISTS analysis_daily_sender_domain (
			    day_key INTEGER NOT NULL,
			    domain_id INTEGER NOT NULL,
			    total INTEGER NOT NULL,
			    delivered INTEGER NOT NULL,
			    underway INTEGER NOT NULL,
			    bounce INTEGER NOT NULL,
			    duration_metrics_version INTEGER NOT NULL DEFAULT 0,
			    duration_count INTEGER NOT NULL DEFAULT 0,
			    duration_sum_seconds INTEGER NOT NULL DEFAULT 0,
			    duration_missing_count INTEGER NOT NULL DEFAULT 0,
			    within_60_count INTEGER NOT NULL DEFAULT 0,
			    within_300_count INTEGER NOT NULL DEFAULT 0,
			    within_900_count INTEGER NOT NULL DEFAULT 0,
			    within_3600_count INTEGER NOT NULL DEFAULT 0,
			    PRIMARY KEY(day_key, domain_id)
			) WITHOUT ROWID;

			CREATE TABLE IF NOT EXISTS analysis_daily_sender_reason (
			    day_key INTEGER NOT NULL,
			    domain_id INTEGER NOT NULL,
			    reason_code INTEGER NOT NULL,
			    total INTEGER NOT NULL,
			    PRIMARY KEY(day_key, domain_id, reason_code)
			) WITHOUT ROWID;

			CREATE TABLE IF NOT EXISTS analysis_daily_recipient_domain (
			    day_key INTEGER NOT NULL,
			    domain_id INTEGER NOT NULL,
			    total INTEGER NOT NULL,
			    delivered INTEGER NOT NULL,
			    underway INTEGER NOT NULL,
			    bounce INTEGER NOT NULL,
			    PRIMARY KEY(day_key, domain_id)
			) WITHOUT ROWID;

			CREATE TABLE IF NOT EXISTS analysis_daily_reason (
			    day_key INTEGER NOT NULL,
			    reason_code INTEGER NOT NULL,
			    response_code INTEGER NOT NULL,
			    total INTEGER NOT NULL,
			    PRIMARY KEY(day_key, reason_code, response_code)
			) WITHOUT ROWID;

			CREATE TABLE IF NOT EXISTS analysis_daily_response (
			    day_key INTEGER NOT NULL,
			    response_code INTEGER NOT NULL,
			    total INTEGER NOT NULL,
			    PRIMARY KEY(day_key, response_code)
			) WITHOUT ROWID;
			""";
		command.ExecuteNonQuery();
		AddColumnIfMissing(connection, "analysis_daily_status", "duration_metrics_version", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_status", "duration_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_status", "duration_sum_seconds", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_status", "duration_missing_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_status", "within_60_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_status", "within_300_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_status", "within_900_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_status", "within_3600_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_sender_domain", "duration_metrics_version", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_sender_domain", "duration_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_sender_domain", "duration_sum_seconds", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_sender_domain", "duration_missing_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_sender_domain", "within_60_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_sender_domain", "within_300_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_sender_domain", "within_900_count", "INTEGER NOT NULL DEFAULT 0");
		AddColumnIfMissing(connection, "analysis_daily_sender_domain", "within_3600_count", "INTEGER NOT NULL DEFAULT 0");
	}
	private static void EnsureAnalysisMetadataTable(SqliteConnection connection)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			CREATE TABLE IF NOT EXISTS analysis_metadata (
			    metadata_key TEXT NOT NULL,
			    metadata_value INTEGER NOT NULL,
			    PRIMARY KEY(metadata_key)
			) WITHOUT ROWID;
			""";
		command.ExecuteNonQuery();
	}

	private static void EnsureMailItemIndexes(SqliteConnection connection)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			CREATE INDEX IF NOT EXISTS ix_mail_log_inspector_items_accepted
			    ON mail_items(accepted_at DESC);
			CREATE INDEX IF NOT EXISTS ix_mail_log_inspector_items_sender_address_accepted_at
			    ON mail_items(sender_address_id, accepted_at DESC);
			CREATE INDEX IF NOT EXISTS ix_mail_log_inspector_items_recipient_address_accepted_at
			    ON mail_items(recipient_address_id, accepted_at DESC);
			CREATE INDEX IF NOT EXISTS ix_mail_log_inspector_items_sender_domain_accepted_at
			    ON mail_items(sender_domain_id, accepted_at DESC);
			CREATE INDEX IF NOT EXISTS ix_mail_log_inspector_items_recipient_domain_accepted_at
			    ON mail_items(recipient_domain_id, accepted_at DESC);
			DROP INDEX IF EXISTS ix_mail_log_inspector_items_import_status;
			""";
		command.ExecuteNonQuery();
	}
	private static bool MailItemsNeedsRebuild(SqliteConnection connection)
	{
		string createSql = ReadCreateTableSql(connection, "mail_items");
		return createSql.Contains("WITHOUT ROWID", StringComparison.OrdinalIgnoreCase) ||
			!HasColumn(connection, "mail_items", "mail_item_id") ||
			!HasColumn(connection, "mail_items", "tracking_key") ||
			!HasColumn(connection, "mail_items", "reason_code") ||
			HasColumn(connection, "mail_items", "bounce_type");
	}

	private static void EnsureImportReasonCountsTable(SqliteConnection connection)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			CREATE TABLE IF NOT EXISTS import_reason_counts (
			    import_id INTEGER NOT NULL,
			    reason_code INTEGER NOT NULL,
			    total INTEGER NOT NULL,
			    PRIMARY KEY (import_id, reason_code)
			) WITHOUT ROWID;
			""";
		command.ExecuteNonQuery();
	}
	private static void AddColumnIfMissing(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
	{
		if (HasColumn(connection, tableName, columnName))
		{
			return;
		}

		SqliteCommand val = connection.CreateCommand();
		try
		{
			((DbCommand)(object)val).CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
			((DbCommand)(object)val).ExecuteNonQuery();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static bool HasTable(SqliteConnection connection, string tableName)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			((DbCommand)(object)val).CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
			val.Parameters.AddWithValue("$name", (object)tableName);
			return Convert.ToInt64(((DbCommand)(object)val).ExecuteScalar() ?? ((object)0L)) > 0;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			((DbCommand)(object)val).CommandText = "PRAGMA table_info(" + tableName + ");";
			SqliteDataReader val2 = val.ExecuteReader();
			try
			{
				while (((DbDataReader)(object)val2).Read())
				{
					if (string.Equals(((DbDataReader)(object)val2).GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
				return false;
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

	private static string ReadCreateTableSql(SqliteConnection connection, string tableName)
	{
		SqliteCommand val = connection.CreateCommand();
		try
		{
			((DbCommand)(object)val).CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name;";
			val.Parameters.AddWithValue("$name", (object)tableName);
			return (string)(((DbCommand)(object)val).ExecuteScalar() ?? string.Empty);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
