using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed partial class MailLogInspectorStore
{
    public bool HasImportedSourceHashReadOnly(string sourceHash)
    {
        if (string.IsNullOrWhiteSpace(sourceHash) || !File.Exists(_databasePath))
        {
            return false;
        }

        using SqliteConnection connection = OpenReadOnlyConnection();
        if (!TableExists(connection, "imports"))
        {
            return false;
        }
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM imports
                WHERE source_hash = $sourceHash
                LIMIT 1
            );
            """;
        command.Parameters.AddWithValue("$sourceHash", sourceHash);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
    }

    public DateTime? ReadLatestDailyImportReportDayReadOnly()
    {
        if (!File.Exists(_databasePath))
        {
            return null;
        }

        using SqliteConnection connection = OpenReadOnlyConnection();
        if (!TableExists(connection, "imports"))
        {
            return null;
        }
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT report_start, report_end
            FROM imports
            WHERE report_start IS NOT NULL
              AND report_end IS NOT NULL
              AND row_count > 0
            ORDER BY report_end DESC, import_id DESC
            LIMIT 200;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            DateTime start = reader.GetDateTime(0);
            DateTime end = reader.GetDateTime(1);
            TimeSpan duration = end - start;
            if (duration > TimeSpan.Zero && duration <= TimeSpan.FromHours(48))
            {
                return end.Date;
            }
        }

        return null;
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = $tableName
            );
            """;
        command.Parameters.AddWithValue("$tableName", tableName);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
    }

    private SqliteConnection OpenReadOnlyConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 30
        }.ToString());
        connection.Open();
        return connection;
    }
}
