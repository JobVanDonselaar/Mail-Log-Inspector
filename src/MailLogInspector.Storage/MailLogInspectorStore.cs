using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MailLogInspector.Core;

namespace MailLogInspector.Storage;

public sealed partial class MailLogInspectorStore
{
	private readonly string _databasePath;

	public string DatabasePath => _databasePath;

	public MailLogInspectorStore(string databasePath)
	{
		_databasePath = Path.GetFullPath(databasePath);
	}

	public void Initialize()
	{
		string? directoryName = Path.GetDirectoryName(_databasePath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		try
		{
			SqliteConnection val = OpenConnection();
			try
			{
				MailLogInspectorSchema.Ensure(val);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch (InvalidOperationException)
		{
			throw;
		}
		catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
		{
			throw new InvalidOperationException("De database wordt momenteel gebruikt door een andere Mail Log Inspector instantie. Sluit andere processen en start opnieuw.", ex);
		}
	}

	public MailLogInspectorDatabaseState GetDatabaseState()
	{
		if (!File.Exists(_databasePath))
		{
			return MailLogInspectorDatabaseState.MissingOrEmpty;
		}

		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = _databasePath,
			Mode = SqliteOpenMode.ReadOnly,
			DefaultTimeout = 30
		}.ToString());
		connection.Open();
		return MailLogInspectorSchema.GetMailItemsState(connection);
	}

	public long CountMailItems()
	{
		SqliteConnection val = OpenConnection();
		try
		{
			SqliteCommand val2 = val.CreateCommand();
			try
			{
				((DbCommand)(object)val2).CommandText = "SELECT COUNT(*) FROM mail_items;";
				return (long)(((DbCommand)(object)val2).ExecuteScalar() ?? ((object)0L));
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

	public bool HasMailInAcceptedRange(DateTime fromInclusive, DateTime throughInclusive)
	{
		SqliteConnection val = OpenConnection();
		try
		{
			SqliteCommand val2 = val.CreateCommand();
			try
			{
				((DbCommand)(object)val2).CommandText = "SELECT EXISTS(\n    SELECT 1\n    FROM mail_items\n    WHERE accepted_at >= $fromInclusive\n      AND accepted_at <= $throughInclusive\n    LIMIT 1\n);";
				val2.Parameters.AddWithValue("$fromInclusive", ToStoredTicks(fromInclusive));
				val2.Parameters.AddWithValue("$throughInclusive", ToStoredTicks(throughInclusive));
				return Convert.ToInt32(((DbCommand)(object)val2).ExecuteScalar() ?? ((object)0)) == 1;
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

	public DateTime? ReadLatestAcceptedAt()
	{
		SqliteConnection val = OpenConnection();
		try
		{
			SqliteCommand val2 = val.CreateCommand();
			try
			{
				((DbCommand)(object)val2).CommandText = "SELECT MAX(accepted_at) FROM mail_items;";
				object? obj = ((DbCommand)(object)val2).ExecuteScalar();
				return obj is null or DBNull ? null : FromStoredTicks(Convert.ToInt64(obj));
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

	public MailLogInspectorDatabaseStats ReadDatabaseStats()
	{
		SqliteConnection val = OpenConnection();
		try
		{
			SqliteCommand val2 = val.CreateCommand();
			try
			{
				((DbCommand)(object)val2).CommandText = "SELECT\n    (SELECT COUNT(*) FROM mail_items),\n    (SELECT COUNT(*) FROM imports),\n    (SELECT MIN(report_start) FROM imports),\n    (SELECT MAX(report_end) FROM imports),\n    (SELECT MAX(imported_at) FROM imports);";
				SqliteDataReader val3 = val2.ExecuteReader();
				try
				{
					((DbDataReader)(object)val3).Read();
					FileInfo fileInfo = new FileInfo(_databasePath);
					FileInfo walFileInfo = new FileInfo(_databasePath + "-wal");
					long databaseBytes =
						(fileInfo.Exists ? fileInfo.Length : 0) +
						(walFileInfo.Exists ? walFileInfo.Length : 0);
					return new MailLogInspectorDatabaseStats(((DbDataReader)(object)val3).GetInt64(0), ((DbDataReader)(object)val3).GetInt64(1), databaseBytes, ReadDateTime(val3, 2), ReadDateTime(val3, 3), ReadDateTime(val3, 4));
				}
				finally
				{
					((IDisposable)val3)?.Dispose();
				}
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

	public IReadOnlyList<MailLogInspectorImportedFile> ReadRecentImports(int limit = 20)
	{
		SqliteConnection val = OpenConnection();
		try
		{
			SqliteCommand val2 = val.CreateCommand();
			try
			{
				((DbCommand)(object)val2).CommandText = "SELECT import_id,\n       source_path,\n       source_file_name,\n       source_hash,\n       imported_at,\n       report_start,\n       report_end,\n       row_count,\n       archive_path,\n       delivered_count,\n       bounce_count,\n       underway_count\nFROM imports\nORDER BY imported_at DESC, import_id DESC\nLIMIT $limit;";
				val2.Parameters.AddWithValue("$limit", (object)limit);
				SqliteDataReader val3 = val2.ExecuteReader();
				try
				{
					List<MailLogInspectorImportedFile> list = new List<MailLogInspectorImportedFile>();
					while (((DbDataReader)(object)val3).Read())
					{
						list.Add(new MailLogInspectorImportedFile(((DbDataReader)(object)val3).GetInt64(0), ((DbDataReader)(object)val3).GetString(1), ((DbDataReader)(object)val3).GetString(2), ((DbDataReader)(object)val3).GetString(3), ((DbDataReader)(object)val3).GetDateTime(4), ReadDateTime(val3, 5), ReadDateTime(val3, 6), ((DbDataReader)(object)val3).GetInt32(7), ((DbDataReader)(object)val3).IsDBNull(8) ? null : ((DbDataReader)(object)val3).GetString(8), ((DbDataReader)(object)val3).GetInt32(9), ((DbDataReader)(object)val3).GetInt32(10), ((DbDataReader)(object)val3).GetInt32(11)));
					}

					Dictionary<long, IReadOnlyList<MailLogInspectorImportCause>> causes = ReadBounceCausesForImports(val, list.Select(import => import.ImportId).ToArray(), 4);
					for (int index = 0; index < list.Count; index++)
					{
						if (causes.TryGetValue(list[index].ImportId, out IReadOnlyList<MailLogInspectorImportCause>? importCauses))
						{
							list[index] = list[index] with { BounceCauses = importCauses };
						}
					}

					return list;
				}
				finally
				{
					((IDisposable)val3)?.Dispose();
				}
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

	private static Dictionary<long, IReadOnlyList<MailLogInspectorImportCause>> ReadBounceCausesForImports(SqliteConnection connection, IReadOnlyList<long> importIds, int limitPerImport)
	{
		Dictionary<long, List<(string Label, int Count)>> grouped = new();
		if (importIds.Count == 0)
		{
			return new Dictionary<long, IReadOnlyList<MailLogInspectorImportCause>>();
		}

		using SqliteCommand command = connection.CreateCommand();
		StringBuilder sql = new();
		sql.Append("SELECT import_id, reason_code, total FROM import_reason_counts WHERE import_id IN (");
		for (int index = 0; index < importIds.Count; index++)
		{
			if (index > 0)
			{
				sql.Append(", ");
			}
			string parameterName = "$importId" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
			sql.Append(parameterName);
			command.Parameters.AddWithValue(parameterName, importIds[index]);
		}
		sql.Append(");");
		command.CommandText = sql.ToString();
		using SqliteDataReader reader = command.ExecuteReader();
		while (reader.Read())
		{
			long importId = reader.GetInt64(0);
			var reason = (MailLogInspectorReasonCode)reader.GetInt32(1);
			int count = reader.GetInt32(2);
			if (!grouped.TryGetValue(importId, out List<(string Label, int Count)>? rows))
			{
				rows = new List<(string Label, int Count)>();
				grouped.Add(importId, rows);
			}
			rows.Add((MailLogInspectorAttemptMeaning.DescribeBounceStatus(reason), count));
		}

		Dictionary<long, IReadOnlyList<MailLogInspectorImportCause>> result = new();
		foreach (KeyValuePair<long, List<(string Label, int Count)>> entry in grouped)
		{
			List<(string Label, int Count)> ordered = entry.Value
				.OrderByDescending(static row => row.Count)
				.ThenBy(static row => row.Label, StringComparer.OrdinalIgnoreCase)
				.Take(limitPerImport)
				.ToList();
			int max = ordered.Count == 0 ? 0 : ordered.Max(static row => row.Count);
			result.Add(entry.Key, ordered.Select(row => new MailLogInspectorImportCause(row.Label, row.Count, max <= 0 ? 0.0 : Math.Round((double)row.Count * 100.0 / max, 1))).ToList());
		}
		return result;
	}
	public void OptimizeForReadPerformance()
	{
		SqliteConnection val = OpenConnection();
		try
		{
			SqliteCommand val2 = val.CreateCommand();
			try
			{
				((DbCommand)(object)val2).CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA optimize; ANALYZE;";
				((DbCommand)(object)val2).ExecuteNonQuery();
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

	public int ApplyRetention(DateTime cutoffDate)
	{
		SqliteConnection val = OpenConnection();
		try
		{
			MailLogInspectorSchema.Ensure(val);
			using SqliteTransaction transaction = val.BeginTransaction();
			SqliteCommand val2 = val.CreateCommand();
			try
			{
				val2.Transaction = transaction;
				((DbCommand)(object)val2).CommandText = "DELETE FROM mail_items\nWHERE accepted_at IS NOT NULL\n  AND accepted_at < $cutoff;";
				val2.Parameters.AddWithValue("$cutoff", ToStoredTicks(cutoffDate.Date));
				int deletedRows = ((DbCommand)(object)val2).ExecuteNonQuery();
				if (deletedRows > 0)
				{
					RebuildAnalysisTables(val, transaction, CancellationToken.None);
				}
				transaction.Commit();
				return deletedRows;
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

	internal SqliteConnection OpenConnection()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected O, but got Unknown
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected O, but got Unknown
		SqliteConnection val = new SqliteConnection(((object)new SqliteConnectionStringBuilder
		{
			DataSource = _databasePath,
			DefaultTimeout = 30
		}).ToString());
		((DbConnection)(object)val).Open();
		SqliteCommand val2 = val.CreateCommand();
		try
		{
			((DbCommand)(object)val2).CommandText = BuildPragmaCommand(_databasePath);
			((DbCommand)(object)val2).ExecuteNonQuery();
			return val;
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
	}

	internal static string BuildPragmaCommand(string databasePath)
	{
		return UsesRemoteOrSharedStorage(databasePath)
			? "PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA temp_store=FILE; PRAGMA cache_size=-65536;"
			: "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=FILE; PRAGMA cache_size=-65536;";
	}

	private static bool UsesRemoteOrSharedStorage(string databasePath)
	{
		string fullPath = Path.GetFullPath(databasePath);
		if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
		{
			return true;
		}

		string? root = Path.GetPathRoot(fullPath);
		if (string.IsNullOrWhiteSpace(root))
		{
			return false;
		}

		try
		{
			DriveInfo drive = new DriveInfo(root);
			return drive.DriveType is DriveType.Network or DriveType.Removable or DriveType.CDRom or DriveType.Ram;
		}
		catch
		{
			return false;
		}
	}

	internal static DateTime? ReadDateTime(SqliteDataReader reader, int ordinal)
	{
		if (!((DbDataReader)(object)reader).IsDBNull(ordinal))
		{
			return ((DbDataReader)(object)reader).GetDateTime(ordinal);
		}
		return null;
	}

}
