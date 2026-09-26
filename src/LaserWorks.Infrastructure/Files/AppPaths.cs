using LaserWorks.Application.Abstractions;

namespace LaserWorks.Infrastructure.Files;

/// <summary>
/// Data location: %LOCALAPPDATA%\LaserWorksManager by default; a "portable.flag" file next to the executable
/// switches to a "data" folder beside the executable (portable ZIP edition).
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? ResolveDefault();
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }

    public static string ResolveDefault()
    {
        // The folder of the .exe itself (not AppContext.BaseDirectory, which is the extraction folder of a single-file build)
        var baseDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "portable.flag"))) return Path.Combine(baseDir, "data");
        var env = Environment.GetEnvironmentVariable("LASERWORKS_DATA");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaserWorksManager");
    }

    public string DataDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "laserworks.db");
    public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string ExportsDirectory => Path.Combine(DataDirectory, "exports");
}

public sealed class FileAttachmentStore : IAttachmentStore
{
    private readonly IAppPaths _paths;

    public FileAttachmentStore(IAppPaths paths) => _paths = paths;

    public async Task<(string StoredPath, long Size)> SaveAsync(string sourceFile, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(sourceFile);
        var folder = DateTime.Now.ToString("yyyy-MM");
        var rel = Path.Combine(folder, $"{Guid.NewGuid():N}{ext}");
        var dest = Path.Combine(_paths.AttachmentsDirectory, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await using (var src = File.OpenRead(sourceFile))
        await using (var dst = File.Create(dest))
            await src.CopyToAsync(dst, ct);
        return (rel.Replace('\\', '/'), new FileInfo(dest).Length);
    }

    public string Resolve(string storedPath) => Path.Combine(_paths.AttachmentsDirectory, storedPath.Replace('/', Path.DirectorySeparatorChar));

    public void Delete(string storedPath)
    {
        try
        {
            var p = Resolve(storedPath);
            if (File.Exists(p)) File.Delete(p);
        }
        catch (IOException)
        {
            // file locked or missing: the database record is already gone; a stale file is harmless
        }
    }
}
