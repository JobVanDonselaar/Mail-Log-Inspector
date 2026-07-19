using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorStoreConnectionTests
{
	[Theory]
	[InlineData(@"C:\Apps\Mail Log Inspector\mail-log-inspector.sqlite", "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=FILE; PRAGMA cache_size=-65536;")]
	[InlineData(@"\\VBoxSvr\Codex\Mail Log Inspector\mail-log-inspector.sqlite", "PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA temp_store=FILE; PRAGMA cache_size=-65536;")]
	public void BuildPragmaCommand_UsesSafeJournalModeForPath(string databasePath, string expected)
	{
		string pragmaCommand = MailLogInspectorStore.BuildPragmaCommand(databasePath);

		Assert.Equal(expected, pragmaCommand);
	}

    [Fact]
    public void Initialize_CreatesCompactRowIdSchemaAndPersistedImportCauses()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-schema-v2-" + Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "mail-log-inspector.sqlite");
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using Microsoft.Data.Sqlite.SqliteCommand sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'mail_items';";
        string tableSql = Convert.ToString(sqlCommand.ExecuteScalar()) ?? string.Empty;
        using Microsoft.Data.Sqlite.SqliteCommand columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "SELECT name FROM pragma_table_info('mail_items') ORDER BY cid;";
        using Microsoft.Data.Sqlite.SqliteDataReader reader = columnsCommand.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Contains("mail_item_id INTEGER PRIMARY KEY", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNIQUE (tracking_key, recipient_address_id)", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WITHOUT ROWID", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bounce_type", columns);
        Assert.Contains("import_reason_counts", ReadTableNames(connection));
    }

    private static IReadOnlyList<string> ReadTableNames(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        using Microsoft.Data.Sqlite.SqliteDataReader reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }}
