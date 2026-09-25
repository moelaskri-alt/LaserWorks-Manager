using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>Creates/migrates the schema and applies connection-level pragmas.</summary>
    public static async Task MigrateAsync(SqliteDbFactory factory, CancellationToken ct = default)
    {
        await using var db = factory.CreateContext();
        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }

    /// <summary>Runs PRAGMA integrity_check and foreign_key_check. Returns problems found (empty = healthy).</summary>
    public static List<string> CheckIntegrity(string databasePath)
    {
        var problems = new List<string>();
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA integrity_check;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var v = r.GetString(0);
                if (!string.Equals(v, "ok", StringComparison.OrdinalIgnoreCase)) problems.Add(v);
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_key_check;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) problems.Add($"Foreign key violation in {r.GetString(0)} row {r.GetValue(1)}");
        }
        return problems;
    }
}
