using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace DiskGrowthMonitor.Core;

public sealed class SqliteMonitorStore
{
    private readonly string _databasePath;

    public SqliteMonitorStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        Execute(connection, """
CREATE TABLE IF NOT EXISTS file_snapshot (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path TEXT NOT NULL UNIQUE,
    size INTEGER NOT NULL,
    last_write_time TEXT NOT NULL,
    last_seen_time TEXT NOT NULL,
    is_deleted INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS growth_event (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_time TEXT NOT NULL,
    path TEXT NOT NULL,
    event_type TEXT NOT NULL,
    old_size INTEGER NOT NULL,
    new_size INTEGER NOT NULL,
    delta_size INTEGER NOT NULL,
    group_path TEXT NOT NULL,
    source_guess TEXT NOT NULL,
    confidence REAL NOT NULL
);
CREATE TABLE IF NOT EXISTS settings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    key TEXT NOT NULL UNIQUE,
    value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ignore_list (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path TEXT NOT NULL UNIQUE,
    created_time TEXT NOT NULL,
    note TEXT NOT NULL
);
""");
    }

    public FileSnapshot? GetSnapshot(string path)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, size, last_write_time, last_seen_time, is_deleted FROM file_snapshot WHERE path = $path";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new FileSnapshot(
            reader.GetString(0),
            reader.GetInt64(1),
            DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetInt32(4) == 1);
    }

    public void UpsertSnapshot(FileSnapshot snapshot)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO file_snapshot(path, size, last_write_time, last_seen_time, is_deleted)
VALUES($path, $size, $last_write_time, $last_seen_time, $is_deleted)
ON CONFLICT(path) DO UPDATE SET
    size = excluded.size,
    last_write_time = excluded.last_write_time,
    last_seen_time = excluded.last_seen_time,
    is_deleted = excluded.is_deleted
""";
        command.Parameters.AddWithValue("$path", snapshot.Path);
        command.Parameters.AddWithValue("$size", snapshot.Size);
        command.Parameters.AddWithValue("$last_write_time", snapshot.LastWriteTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$last_seen_time", snapshot.LastSeenTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$is_deleted", snapshot.IsDeleted ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void InsertGrowthEvents(IEnumerable<GrowthEvent> events)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var item in events)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO growth_event(event_time, path, event_type, old_size, new_size, delta_size, group_path, source_guess, confidence)
VALUES($event_time, $path, $event_type, $old_size, $new_size, $delta_size, $group_path, $source_guess, $confidence)
""";
            command.Parameters.AddWithValue("$event_time", item.EventTime.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$path", item.Path);
            command.Parameters.AddWithValue("$event_type", item.EventType.ToString());
            command.Parameters.AddWithValue("$old_size", item.OldSize);
            command.Parameters.AddWithValue("$new_size", item.NewSize);
            command.Parameters.AddWithValue("$delta_size", item.DeltaSize);
            command.Parameters.AddWithValue("$group_path", item.GroupPath);
            command.Parameters.AddWithValue("$source_guess", item.SourceGuess);
            command.Parameters.AddWithValue("$confidence", item.Confidence);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO settings(key, value) VALUES($key, $value)
ON CONFLICT(key) DO UPDATE SET value = excluded.value
""";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public string? GetSetting(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void AddIgnorePath(string path, string note)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO ignore_list(path, created_time, note) VALUES($path, $created_time, $note)
ON CONFLICT(path) DO UPDATE SET note = excluded.note
""";
        command.Parameters.AddWithValue("$path", PathRules.NormalizeDirectory(path));
        command.Parameters.AddWithValue("$created_time", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$note", note);
        command.ExecuteNonQuery();
    }

    public void RemoveIgnorePath(string path)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ignore_list WHERE path = $path";
        command.Parameters.AddWithValue("$path", PathRules.NormalizeDirectory(path));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<IgnoreEntry> GetIgnoreList()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, path, created_time, note FROM ignore_list ORDER BY created_time DESC";
        using var reader = command.ExecuteReader();
        var rows = new List<IgnoreEntry>();
        while (reader.Read())
        {
            rows.Add(new IgnoreEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(3)));
        }
        return rows;
    }

    public bool IsIgnored(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return GetIgnoreList().Any(item => PathRules.IsSameOrChild(fullPath, item.Path));
    }

    public IReadOnlyList<GrowthAggregate> QueryAggregates(TimeSpan window, long displayThresholdBytes)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT group_path,
       SUM(delta_size) AS total_delta,
       MIN(event_time) AS first_seen,
       MAX(event_time) AS last_seen,
       COUNT(*) AS event_count,
       source_guess,
       MAX(confidence) AS confidence
FROM growth_event
WHERE event_time >= $since AND delta_size != 0
GROUP BY group_path, source_guess
HAVING ABS(total_delta) >= $threshold
ORDER BY ABS(total_delta) DESC
LIMIT 50
""";
        command.Parameters.AddWithValue("$since", DateTime.Now.Subtract(window).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$threshold", displayThresholdBytes);
        using var reader = command.ExecuteReader();
        var rows = new List<GrowthAggregate>();
        while (reader.Read())
        {
            rows.Add(new GrowthAggregate(
                reader.GetString(0),
                reader.GetInt64(1),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetDouble(6)));
        }
        return rows;
    }

    public IReadOnlyList<GrowthEvent> QueryEventsForGroup(string groupPath, TimeSpan window, int limit = 12)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, event_time, path, event_type, old_size, new_size, delta_size, group_path, source_guess, confidence
FROM growth_event
WHERE event_time >= $since AND group_path = $group_path
ORDER BY event_time DESC
LIMIT $limit
""";
        command.Parameters.AddWithValue("$since", DateTime.Now.Subtract(window).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$group_path", groupPath);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<GrowthEvent>();
        while (reader.Read())
        {
            rows.Add(ReadGrowthEvent(reader));
        }
        return rows;
    }

    public IReadOnlyList<GrowthEvent> QueryEventsUnderGroup(string groupPath, TimeSpan window, int limit = 300)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, event_time, path, event_type, old_size, new_size, delta_size, group_path, source_guess, confidence
FROM growth_event
WHERE event_time >= $since AND (group_path = $group_path OR path LIKE $prefix)
ORDER BY ABS(delta_size) DESC, event_time DESC
LIMIT $limit
""";
        command.Parameters.AddWithValue("$since", DateTime.Now.Subtract(window).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$group_path", groupPath);
        command.Parameters.AddWithValue("$prefix", groupPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "\\%");
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<GrowthEvent>();
        while (reader.Read())
        {
            rows.Add(ReadGrowthEvent(reader));
        }
        return rows;
    }

    public IReadOnlyList<GrowthEvent> QueryRecentEvents(int limit = 200)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, event_time, path, event_type, old_size, new_size, delta_size, group_path, source_guess, confidence
FROM growth_event
ORDER BY event_time DESC
LIMIT $limit
""";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<GrowthEvent>();
        while (reader.Read())
        {
            rows.Add(ReadGrowthEvent(reader));
        }
        return rows;
    }

    public string ExportCsv(TimeSpan window, string exportDirectory)
    {
        Directory.CreateDirectory(exportDirectory);
        var output = Path.Combine(exportDirectory, $"growth-events-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, event_time, path, event_type, old_size, new_size, delta_size, group_path, source_guess, confidence
FROM growth_event
WHERE event_time >= $since
ORDER BY event_time DESC
""";
        command.Parameters.AddWithValue("$since", DateTime.Now.Subtract(window).ToString("O", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        using var writer = new StreamWriter(output, false, new UTF8Encoding(true));
        writer.WriteLine("id,event_time,path,event_type,old_size,new_size,delta_size,group_path,source_guess,confidence");
        while (reader.Read())
        {
            var item = ReadGrowthEvent(reader);
            writer.WriteLine(string.Join(",", new[]
            {
                item.Id.ToString(CultureInfo.InvariantCulture),
                Csv(item.EventTime.ToString("O", CultureInfo.InvariantCulture)),
                Csv(item.Path),
                Csv(item.EventType.ToString()),
                item.OldSize.ToString(CultureInfo.InvariantCulture),
                item.NewSize.ToString(CultureInfo.InvariantCulture),
                item.DeltaSize.ToString(CultureInfo.InvariantCulture),
                Csv(item.GroupPath),
                Csv(item.SourceGuess),
                item.Confidence.ToString(CultureInfo.InvariantCulture)
            }));
        }
        return output;
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static GrowthEvent ReadGrowthEvent(SqliteDataReader reader)
    {
        return new GrowthEvent(
            reader.GetInt64(0),
            DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(2),
            Enum.Parse<GrowthEventType>(reader.GetString(3)),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetDouble(9));
    }

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
