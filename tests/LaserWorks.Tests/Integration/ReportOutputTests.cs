using System.Text.RegularExpressions;
using ClosedXML.Excel;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Localization;
using LaserWorks.Reporting.Core;
using LaserWorks.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Integration;

/// <summary>
/// Report output must show business values only: every cell of every report (with and without filters, for every job,
/// in Arabic and English) holds a display-ready value (text, number, date, enum, yes/no) and its formatted text —
/// as shown on screen and written to PDF, Excel and CSV — never contains an object's type name (System.Object,
/// collections, anonymous types, entity class names), a raw internal code or an exception message.
/// </summary>
public class ReportOutputTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _out;
    private TestDb _t = null!;
    public ReportOutputTests(ITestOutputHelper output) => _out = output;

    public async Task InitializeAsync() => _t = await TestDb.CreateAsync(demo: true, now: DateTime.Today.AddHours(9));
    public async Task DisposeAsync()
    {
        Loc.Instance.SetLanguage("en");
        await _t.DisposeAsync();
    }

    private static readonly Type[] DisplayTypes = { typeof(string), typeof(decimal), typeof(int), typeof(long), typeof(DateTime), typeof(bool) };

    /// <summary>Text that betrays a serialized object instead of a business value.</summary>
    public static readonly Regex Artifact = new(@"System\.|Microsoft\.|LaserWorks\.|Castle\.|AnonymousType|<>f__|\bnull\b|Exception|\{\s*\w+\s*=", RegexOptions.Compiled);

    /// <summary>Internal document/source codes written by the services; they must be shown through their translated names.</summary>
    public static readonly string[] SourceCodes =
    {
        "CustomerPayment", "DirectCost", "DirectCostReversal", "Expense", "InventoryTx", "Job", "JobComponent", "JobOperation", "Manual", "Opening",
        "Overhead", "PurchaseReceipt", "PurchaseReturn", "Remnant", "SalesInvoice", "SalesReturn", "Scrap", "SupplierInvoice", "SupplierPayment"
    };

    /// <summary>Entity type names (audit trail) and sequence keys — also internal codes that must never be shown raw.</summary>
    public static readonly string[] InternalNames = typeof(LaserWorks.Domain.Entities.Job).Assembly.GetTypes()
        .Where(t => typeof(LaserWorks.Domain.Common.IAudited).IsAssignableFrom(t) && !t.IsInterface).Select(t => t.Name)
        .Concat(Enum.GetNames<LaserWorks.Domain.Enums.SequenceKey>()).Concat(SourceCodes).Distinct().ToArray();

    /// <summary>True when the text is an object artifact, or a raw internal code / type name that is not a real label in the current language.</summary>
    public static bool IsArtifact(string text) =>
        Artifact.IsMatch(text) || (InternalNames.Contains(text) && !Loc.Instance.Strings(Loc.Instance.Language).Values.Contains(text));

    public static IEnumerable<string> Problems(string report, ReportTable table)
    {
        var loc = Loc.Instance;
        foreach (var c in table.Columns)
            if (!loc.Has(c.HeaderKey)) yield return $"{report}: header key {c.HeaderKey} missing";
        var ri = 0;
        foreach (var row in table.Rows)
        {
            ri++;
            if (row.Cells.Count != table.Columns.Count) yield return $"{report} row {ri}: {row.Cells.Count} cells for {table.Columns.Count} columns";
            for (var i = 0; i < row.Cells.Count && i < table.Columns.Count; i++)
            {
                var v = row.Cells[i];
                if (v != null && !DisplayTypes.Contains(v.GetType()) && v is not Enum)
                    yield return $"{report} row {ri} [{table.Columns[i].Key}]: value of type {v.GetType().FullName}";
                var text = CellFormatter.Format(v, table.Columns[i].Kind);
                if (IsArtifact(text)) yield return $"{report} row {ri} [{table.Columns[i].Key}]: \"{text}\"";
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task No_report_shows_object_names_or_internal_codes(string lang)
    {
        Loc.Instance.SetLanguage(lang);
        var catalog = _t.Get<ReportCatalog>();
        var company = await _t.Get<SettingsService>().GetAsync();
        List<long> jobs; long customerId, accountId;
        await using (var db = _t.Get<IAppDbFactory>().Create())
        {
            jobs = await db.Jobs.Select(j => j.Id).ToListAsync();
            customerId = await db.Customers.Select(c => c.Id).FirstAsync();
            accountId = await db.Accounts.Where(a => a.SystemKey == SystemAccounts.Bank).Select(a => a.Id).FirstAsync();
        }
        var today = DateTime.Today;
        var range = new DateRange(today.AddYears(-1), today);
        var problems = new List<string>();
        var tables = 0; var cells = 0;
        async Task Check(string key, ReportFilter f)
        {
            var table = await catalog.RunAsync(key, f);
            tables++;
            cells += table.Rows.Sum(r => r.Cells.Count);
            problems.AddRange(Problems(key, table));
            // the Excel and CSV files written from the table carry the same text
            if (table.Rows.Count == 0) return;
            var xlsx = Path.Combine(_t.Folder, $"{key}-{lang}-{tables}.xlsx");
            ExcelExporter.Export(table, company, xlsx);
            var headers = table.Columns.Select(c => Loc.Instance[c.HeaderKey]).ToHashSet();
            using (var wb = new XLWorkbook(xlsx))
                foreach (var cell in wb.Worksheet(1).CellsUsed())
                    if (!headers.Contains(cell.GetFormattedString()) && IsArtifact(cell.GetFormattedString())) problems.Add($"{key} xlsx {cell.Address}: \"{cell.GetFormattedString()}\"");
            var csv = Path.Combine(_t.Folder, $"{key}-{lang}-{tables}.csv");
            CsvExporter.Export(table, csv);
            foreach (var line in File.ReadAllLines(csv).Skip(1))
                foreach (var part in line.Split(','))
                    if (IsArtifact(part.Trim('"'))) problems.Add($"{key} csv: \"{part}\"");
        }

        foreach (var r in catalog.Reports)
        {
            if (r.Required == ReportFilterKind.None)
                await Check(r.Key, new ReportFilter(range, today));
            await Check(r.Key, new ReportFilter(range, today, CustomerId: customerId, JobId: jobs[0], AccountId: accountId));
        }
        // job drill-down reports for every job of the demo company
        foreach (var jobId in jobs)
            foreach (var key in new[] { "JobCostSheet", "EstimatedVsActual", "JobComponents" })
                await Check(key, new ReportFilter(range, today, JobId: jobId));

        _out.WriteLine($"{tables} report tables, {cells} cells checked, {problems.Count} problems");
        foreach (var p in problems.Distinct().Take(80)) _out.WriteLine(p);
        Assert.Empty(problems.Distinct());
    }

    [Fact]
    public void Every_source_code_has_a_translated_name()
    {
        foreach (var lang in new[] { "en", "ar" })
            foreach (var code in SourceCodes.Append("DirectCostReversal"))
                Assert.True(Loc.Instance.Strings(lang).ContainsKey("Source." + code), $"{lang}: Source.{code}");
        // every code the services write is in the list above
        var src = File.ReadAllText(Path.Combine(Root(), "src", "LaserWorks.Application", "Services", "Engines.cs"));
        foreach (var file in Directory.GetFiles(Path.Combine(Root(), "src", "LaserWorks.Application", "Services"), "*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"SourceType\s*[=:]\s*""([A-Za-z]+)""|JobCostEngine\.Add\(db,\s*\w+,\s*[^,]+,\s*[^,]+,\s*[^,]+,\s*""([A-Za-z]+)"""))
            {
                var code = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                Assert.True(SourceCodes.Contains(code) || code == "DirectCostReversal", $"{Path.GetFileName(file)}: source code {code} has no display name");
            }
    }

    [Fact]
    public void Every_audited_entity_and_sequence_has_a_translated_name()
    {
        var audited = typeof(LaserWorks.Domain.Entities.Job).Assembly.GetTypes().Where(t => typeof(LaserWorks.Domain.Common.IAudited).IsAssignableFrom(t) && !t.IsInterface).Select(t => t.Name)
            .Concat(new[] { "Attachment", "FiscalPeriod", "JobOperation", "JournalEntry", "RolePermission" });
        foreach (var lang in new[] { "en", "ar" })
        {
            foreach (var name in audited) Assert.True(Loc.Instance.Strings(lang).ContainsKey("Entity." + name), $"{lang}: Entity.{name}");
            foreach (var key in Enum.GetNames<LaserWorks.Domain.Enums.SequenceKey>()) Assert.True(Loc.Instance.Strings(lang).ContainsKey("Enum.SequenceKey." + key), $"{lang}: sequence {key}");
        }
    }

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "LaserWorksManager.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void The_check_catches_object_artifacts()
    {
        var t = new ReportTable().Col("a", "Col.Description").Col("b", "Col.Source");
        t.Add(new object(), "JobOperation");
        t.Add(new List<int>(), new { A = 1 });
        var p = Problems("probe", t).ToList();
        Assert.Contains(p, x => x.Contains("System.Object"));
        Assert.Contains(p, x => x.Contains("JobOperation"));
        Assert.Contains(p, x => x.Contains("List"));
        Assert.Contains(p, x => x.Contains("AnonymousType") || x.Contains("<>f__"));
    }
}
