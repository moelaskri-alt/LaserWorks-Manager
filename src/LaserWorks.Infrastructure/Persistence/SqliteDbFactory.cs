using LaserWorks.Application.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LaserWorks.Infrastructure.Persistence;

public sealed class SqliteDbFactory : IAppDbFactory
{
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public string ConnectionString { get; }

    public SqliteDbFactory(string databasePath, ICurrentUser user, IClock clock)
    {
        ConnectionString = BuildConnectionString(databasePath);
        _options = BuildOptions(ConnectionString);
        _user = user;
        _clock = clock;
    }

    public static string BuildConnectionString(string databasePath) => new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        ForeignKeys = true,
        Cache = SqliteCacheMode.Private,
        Pooling = true,
        DefaultTimeout = 30
    }.ToString();

    public static DbContextOptions<AppDbContext> BuildOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString, o => o.CommandTimeout(60))
            .Options;

    public AppDbContext CreateContext() => new(_options, _user, _clock);

    public IAppDb Create() => CreateContext();
}

/// <summary>Used by `dotnet ef` at design time.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(SqliteDbFactory.BuildOptions(SqliteDbFactory.BuildConnectionString("design.db")));
}
