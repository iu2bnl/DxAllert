using System.Globalization;
using System.Text.Json;
using DXAllert.Core;
using Microsoft.Data.Sqlite;

namespace DXAllert.Data;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DXAllert");
    public static string Database => Path.Combine(DataDir, "dxallert.db");
    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string CtyFile => Path.Combine(DataDir, "cty.dat");
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.Settings;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex) { Log.Error("Lettura impostazioni non riuscita, uso i valori predefiniti", ex); }
        return new AppSettings();
    }

    public static void Save(AppSettings s, string? path = null)
    {
        path ??= AppPaths.Settings;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(s, Json));
    }
}

/// <summary>Database SQLite locale: cache delle fonti, storico spot, nominativi già visti.</summary>
public sealed class Repository
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    public Repository(string? dbPath = null)
    {
        dbPath ??= AppPaths.Database;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        Exec("""
            CREATE TABLE IF NOT EXISTS source_cache(
                source TEXT PRIMARY KEY, fetched_utc TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS spots(
                id INTEGER PRIMARY KEY AUTOINCREMENT, time_utc TEXT NOT NULL, dx_call TEXT NOT NULL,
                freq_khz REAL NOT NULL, spotter TEXT NOT NULL, comment TEXT NOT NULL, source TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_spots_call_time ON spots(dx_call, time_utc);
            CREATE TABLE IF NOT EXISTS known_calls(key TEXT PRIMARY KEY, first_seen_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS worked_log(
                id INTEGER PRIMARY KEY,
                callsign TEXT NOT NULL, band TEXT NOT NULL, mode TEXT NOT NULL, dxcc_name TEXT NOT NULL);
            """);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void Exec(string sql, params (string, object)[] args)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            cmd.ExecuteNonQuery();
        }
    }

    public void SaveSourceCache(string source, IReadOnlyList<Dxpedition> items, DateTime fetchedUtc) =>
        Exec("INSERT INTO source_cache(source, fetched_utc, json) VALUES($s,$t,$j) " +
             "ON CONFLICT(source) DO UPDATE SET fetched_utc=$t, json=$j",
            ("$s", source), ("$t", fetchedUtc.ToString("o")), ("$j", JsonSerializer.Serialize(items)));

    public (List<Dxpedition> Items, DateTime FetchedUtc)? LoadSourceCache(string source)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT fetched_utc, json FROM source_cache WHERE source=$s";
            cmd.Parameters.AddWithValue("$s", source);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var items = JsonSerializer.Deserialize<List<Dxpedition>>(r.GetString(1)) ?? new();
            return (items, DateTime.Parse(r.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }
    }

    public void AddSpot(Spot s) =>
        Exec("INSERT INTO spots(time_utc, dx_call, freq_khz, spotter, comment, source) VALUES($t,$c,$f,$s,$m,$o)",
            ("$t", s.TimeUtc.ToString("o")), ("$c", s.DxCall), ("$f", s.FreqKhz), ("$s", s.Spotter),
            ("$m", s.Comment), ("$o", s.Source));

    public List<Spot> RecentSpots(string dxCall, int limit = 10)
    {
        var list = new List<Spot>();
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT time_utc, dx_call, freq_khz, spotter, comment, source FROM spots " +
                              "WHERE dx_call=$c ORDER BY time_utc DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$c", dxCall);
            cmd.Parameters.AddWithValue("$n", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new Spot(
                    DateTime.Parse(r.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    r.GetString(1), r.GetDouble(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        }
        return list;
    }

    /// <summary>Elimina gli spot più vecchi di N giorni.</summary>
    public void PurgeSpots(int days = 30) =>
        Exec("DELETE FROM spots WHERE time_utc < $t", ("$t", DateTime.UtcNow.AddDays(-days).ToString("o")));

    public bool HasKnownCalls()
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM known_calls)";
            return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
        }
    }

    /// <summary>Registra la chiave; true se non era mai stata vista.</summary>
    public bool MarkKnown(string key)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO known_calls(key, first_seen_utc) VALUES($k,$t)";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    // ---------------------------------------------------------------- log ADIF importato

    /// <summary>Sostituisce l'intero log con i nuovi QSO (in un'unica transazione).</summary>
    public void ReplaceWorkedLog(IEnumerable<WorkedEntry> entries)
    {
        lock (_gate)
        {
            using var c = Open();
            using var t = c.BeginTransaction();
            using var del = c.CreateCommand();
            del.CommandText = "DELETE FROM worked_log";
            del.ExecuteNonQuery();
            using var ins = c.CreateCommand();
            ins.CommandText = "INSERT INTO worked_log(callsign,band,mode,dxcc_name) VALUES($c,$b,$m,$e)";
            foreach (var e in entries)
            {
                ins.Parameters.Clear();
                ins.Parameters.AddWithValue("$c", e.Callsign);
                ins.Parameters.AddWithValue("$b", e.Band);
                ins.Parameters.AddWithValue("$m", e.Mode);
                ins.Parameters.AddWithValue("$e", e.DxccName);
                ins.ExecuteNonQuery();
            }
            t.Commit();
        }
    }

    public List<WorkedEntry> GetWorkedLog()
    {
        var list = new List<WorkedEntry>();
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT callsign, band, mode, dxcc_name FROM worked_log";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new WorkedEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }
        return list;
    }

    public void ClearWorkedLog() => Exec("DELETE FROM worked_log");

    public int CountWorkedLog()
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM worked_log";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}
