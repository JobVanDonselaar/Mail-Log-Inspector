using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed class SmtpApiOperationalStore
{
    private readonly string _databasePath;

    public SmtpApiOperationalStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public void Initialize()
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS smtp_api_config (
                config_id INTEGER PRIMARY KEY CHECK (config_id = 1),
                encrypted_api_key TEXT NULL,
                channel TEXT NULL,
                report_syntax_1 TEXT NULL,
                report_syntax_2 TEXT NULL,
                report_syntax_3 TEXT NULL,
                connection_status TEXT NULL,
                last_successful_use_at_utc TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public SmtpApiConfig LoadConfig()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT encrypted_api_key,
                   channel,
                   report_syntax_1,
                   report_syntax_2,
                   report_syntax_3,
                   connection_status,
                   last_successful_use_at_utc
            FROM smtp_api_config
            WHERE config_id = 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new SmtpApiConfig(
                ReadNullableString(reader, 0),
                ReadNullableString(reader, 1),
                ReadNullableString(reader, 2),
                ReadNullableString(reader, 3),
                ReadNullableString(reader, 4),
                ReadNullableString(reader, 5),
                ReadNullableDateTime(reader, 6))
            : SmtpApiConfig.Empty;
    }

    public void SaveConfig(SmtpApiConfig config)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO smtp_api_config (
                config_id,
                encrypted_api_key,
                channel,
                report_syntax_1,
                report_syntax_2,
                report_syntax_3,
                connection_status,
                last_successful_use_at_utc
            )
            VALUES (
                1,
                $encryptedApiKey,
                $channel,
                $reportSyntax1,
                $reportSyntax2,
                $reportSyntax3,
                $connectionStatus,
                $lastSuccessfulUseAtUtc
            )
            ON CONFLICT(config_id) DO UPDATE SET
                encrypted_api_key = excluded.encrypted_api_key,
                channel = excluded.channel,
                report_syntax_1 = excluded.report_syntax_1,
                report_syntax_2 = excluded.report_syntax_2,
                report_syntax_3 = excluded.report_syntax_3,
                connection_status = excluded.connection_status,
                last_successful_use_at_utc = excluded.last_successful_use_at_utc;
            """;
        command.Parameters.AddWithValue("$encryptedApiKey", ToDbValue(config.EncryptedApiKey));
        command.Parameters.AddWithValue("$channel", ToDbValue(config.Channel));
        command.Parameters.AddWithValue("$reportSyntax1", ToDbValue(config.ReportSyntax1));
        command.Parameters.AddWithValue("$reportSyntax2", ToDbValue(config.ReportSyntax2));
        command.Parameters.AddWithValue("$reportSyntax3", ToDbValue(config.ReportSyntax3));
        command.Parameters.AddWithValue("$connectionStatus", ToDbValue(config.ConnectionStatus));
        command.Parameters.AddWithValue("$lastSuccessfulUseAtUtc", ToDbValue(config.LastSuccessfulUseAtUtc));
        command.ExecuteNonQuery();
    }

    public void RecordSuccessfulUse(DateTime usedAtUtc)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO smtp_api_config (
                config_id,
                last_successful_use_at_utc
            )
            VALUES (1, $usedAtUtc)
            ON CONFLICT(config_id) DO UPDATE SET
                last_successful_use_at_utc = excluded.last_successful_use_at_utc;
            """;
        command.Parameters.AddWithValue(
            "$usedAtUtc",
            NormalizeUtc(usedAtUtc).ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static object ToDbValue(DateTime? value)
    {
        return value.HasValue
            ? NormalizeUtc(value.Value).ToString("O", CultureInfo.InvariantCulture)
            : DBNull.Value;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
