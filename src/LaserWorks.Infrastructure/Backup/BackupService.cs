using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LaserWorks.Application.Abstractions;
using LaserWorks.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace LaserWorks.Infrastructure.Backup;

public sealed record BackupManifest(string AppVersion, string SchemaVersion, DateTime CreatedAt, string CreatedBy, string DatabaseSha256, long DatabaseSize, int AttachmentCount, string? Note);

public sealed record BackupInfo(string FilePath, DateTime CreatedAt, long SizeBytes, string Kind, string? Note, bool Validated);

public sealed record BackupValidation(bool IsValid, IReadOnlyList<string> Problems, BackupManifest? Manifest);

/// <summary>
/// Backup file (*.lwbak) = ZIP containing manifest.json, laserworks.db (consistent snapshot via the SQLite online backup API) and attachments/.
/// Restore always validates the file and takes an automatic safety backup of the current data first.
/// </summary>
public sealed class BackupService
{
    public const string Extension = ".lwbak";
    private readonly IAppPaths _paths;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public BackupService(IAppPaths paths, ICurrentUser user, IClock clock)
    {
        _paths = paths;
        _user = user;
        _clock = clock;
    }

    private string HistoryFile => Path.Combine(_paths.DataDirectory, "backup-history.json");

    public static string AppVersion => typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    private static string LatestMigration(string dbPath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1";
        return cmd.ExecuteScalar() as string ?? "";
    }

    public async Task<BackupInfo> CreateBackupAsync(string? destinationFile = null, string? note = null, string kind = "Manual", CancellationToken ct = default)
    {
        var stamp = _clock.Now;
        destinationFile ??= Path.Combine(_paths.BackupsDirectory, $"LaserWorks_{stamp:yyyyMMdd_HHmmss}{(kind == "Manual" ? "" : "_" + kind)}{Extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        var tempDb = Path.Combine(Path.GetTempPath(), $"lw_backup_{Guid.NewGuid():N}.db");
        try
        {
            await Task.Run(() =>
            {
                using var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath, Pooling = false }.ToString());
                using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tempDb, Pooling = false }.ToString());
                src.Open();
                dst.Open();
                src.BackupDatabase(dst);
            }, ct);
            var hash = await Sha256Async(tempDb);
            var attachments = Directory.Exists(_paths.AttachmentsDirectory) ? Directory.GetFiles(_paths.AttachmentsDirectory, "*", SearchOption.AllDirectories) : Array.Empty<string>();
            var manifest = new BackupManifest(AppVersion, LatestMigration(tempDb), stamp, _user.Username, hash, new FileInfo(tempDb).Length, attachments.Length, note);
            var tmpZip = destinationFile + ".tmp";
            if (File.Exists(tmpZip)) File.Delete(tmpZip);
            using (var zip = ZipFile.Open(tmpZip, ZipArchiveMode.Create))
            {
                var me = zip.CreateEntry("manifest.json");
                await using (var s = me.Open())
                    await JsonSerializer.SerializeAsync(s, manifest, new JsonSerializerOptions { WriteIndented = true }, ct);
                zip.CreateEntryFromFile(tempDb, "laserworks.db", CompressionLevel.Optimal);
                foreach (var f in attachments)
                    zip.CreateEntryFromFile(f, "attachments/" + Path.GetRelativePath(_paths.AttachmentsDirectory, f).Replace('\\', '/'), CompressionLevel.Fastest);
            }
            File.Move(tmpZip, destinationFile, overwrite: true);
            var validation = await ValidateAsync(destinationFile, ct);
            if (!validation.IsValid) throw new InvalidOperationException("Backup verification failed: " + string.Join("; ", validation.Problems));
            var info = new BackupInfo(destinationFile, stamp, new FileInfo(destinationFile).Length, kind, note, true);
            await AppendHistoryAsync(info);
            return info;
        }
        finally
        {
            TryDelete(tempDb);
        }
    }

    public async Task<BackupValidation> ValidateAsync(string backupFile, CancellationToken ct = default)
    {
        var problems = new List<string>();
        BackupManifest? manifest = null;
        if (!File.Exists(backupFile)) return new BackupValidation(false, new[] { "File not found" }, null);
        var tempDb = Path.Combine(Path.GetTempPath(), $"lw_validate_{Guid.NewGuid():N}.db");
        try
        {
            using var zip = ZipFile.OpenRead(backupFile);
            var me = zip.GetEntry("manifest.json");
            if (me == null) problems.Add("manifest.json missing");
            else
            {
                await using var s = me.Open();
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(s, cancellationToken: ct);
            }
            var dbe = zip.GetEntry("laserworks.db");
            if (dbe == null) problems.Add("database missing");
            else
            {
                dbe.ExtractToFile(tempDb, true);
                var hash = await Sha256Async(tempDb);
                if (manifest != null && !string.Equals(hash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase)) problems.Add("database checksum mismatch");
                problems.AddRange(DatabaseInitializer.CheckIntegrity(tempDb));
                using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tempDb, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                conn.Open();
                foreach (var table in new[] { "__EFMigrationsHistory", "CompanySettings", "Jobs", "JournalEntries", "InventoryTransactions", "Users" })
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n";
                    cmd.Parameters.AddWithValue("$n", table);
                    if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) problems.Add($"table {table} missing");
                }
                if (manifest != null)
                {
                    var known = MigrationCatalog.All;
                    if (known.Count > 0 && !known.Contains(manifest.SchemaVersion))
                        problems.Add($"backup schema {manifest.SchemaVersion} is newer than or unknown to this version");
                }
            }
        }
        catch (InvalidDataException)
        {
            problems.Add("not a valid backup archive");
        }
        catch (SqliteException ex)
        {
            problems.Add("database unreadable: " + ex.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(tempDb);
        }
        return new BackupValidation(problems.Count == 0, problems, manifest);
    }

    /// <summary>
    /// Restores a backup: validate → safety backup of current data → replace database and attachments.
    /// All pooled connections are cleared first. The caller must re-run migrations and reload the session afterwards.
    /// </summary>
    public async Task<BackupInfo> RestoreAsync(string backupFile, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(backupFile, ct);
        if (!validation.IsValid) throw new InvalidOperationException("Invalid backup: " + string.Join("; ", validation.Problems));
        var safety = await CreateBackupAsync(note: $"Automatic safety backup before restoring {Path.GetFileName(backupFile)}", kind: "PreRestore", ct: ct);

        SqliteConnection.ClearAllPools();
        var staging = Path.Combine(_paths.DataDirectory, $"restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            ZipFile.ExtractToDirectory(backupFile, staging);
            var stagedDb = Path.Combine(staging, "laserworks.db");
            foreach (var suffix in new[] { "-wal", "-shm" }) TryDelete(_paths.DatabasePath + suffix);
            File.Copy(stagedDb, _paths.DatabasePath, overwrite: true);
            var stagedAtt = Path.Combine(staging, "attachments");
            if (Directory.Exists(_paths.AttachmentsDirectory)) Directory.Delete(_paths.AttachmentsDirectory, true);
            if (Directory.Exists(stagedAtt)) Directory.Move(stagedAtt, _paths.AttachmentsDirectory);
            else Directory.CreateDirectory(_paths.AttachmentsDirectory);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch (IOException) { }
        }
        var problems = DatabaseInitializer.CheckIntegrity(_paths.DatabasePath);
        if (problems.Count > 0) throw new InvalidOperationException("Restored database failed integrity check: " + string.Join("; ", problems));
        var info = new BackupInfo(backupFile, _clock.Now, new FileInfo(backupFile).Length, "Restore", $"Restored (safety backup: {Path.GetFileName(safety.FilePath)})", true);
        await AppendHistoryAsync(info);
        return info;
    }

    public async Task<List<BackupInfo>> HistoryAsync()
    {
        if (!File.Exists(HistoryFile)) return new();
        try
        {
            await using var s = File.OpenRead(HistoryFile);
            return (await JsonSerializer.DeserializeAsync<List<BackupInfo>>(s) ?? new()).OrderByDescending(b => b.CreatedAt).ToList();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private async Task AppendHistoryAsync(BackupInfo info)
    {
        var list = await HistoryAsync();
        list.Insert(0, info);
        await File.WriteAllTextAsync(HistoryFile, JsonSerializer.Serialize(list.Take(500).ToList(), new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Integrity check of the live database (PRAGMA integrity_check and foreign keys).</summary>
    public List<string> CheckLiveDatabase() => DatabaseInitializer.CheckIntegrity(_paths.DatabasePath);

    private static async Task<string> Sha256Async(string file)
    {
        await using var s = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(s));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}

/// <summary>Migration ids compiled into this build (used to reject backups from newer versions).</summary>
public static class MigrationCatalog
{
    public static IReadOnlyCollection<string> All { get; } = typeof(AppDbContext).Assembly.GetTypes()
        .Select(t => t.GetCustomAttributes(typeof(Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute), false).FirstOrDefault())
        .OfType<Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute>().Select(a => a.Id).ToHashSet();
}
