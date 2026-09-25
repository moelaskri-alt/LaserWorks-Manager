using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using LaserWorks.Application.Abstractions;
using LaserWorks.Application.Common;
using LaserWorks.Application.Services;
using LaserWorks.Localization;
using LaserWorks.Reporting.Core;
using LaserWorks.Reporting.Documents;
using LaserWorks.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LaserWorks.Tests.Integration;

/// <summary>Runs every report on the demo company and exports it to PDF, Excel and CSV, in both languages.</summary>
public class ReportTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _out;
    private TestDb _t = null!;
    public ReportTests(ITestOutputHelper output) => _out = output;

    public async Task InitializeAsync() => _t = await TestDb.CreateAsync(demo: true, now: DateTime.Today.AddHours(9));
    public async Task DisposeAsync()
    {
        Loc.Instance.SetLanguage("en");
        await _t.DisposeAsync();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task All_reports_run_and_export(string lang)
    {
        Loc.Instance.SetLanguage(lang);
        var catalog = _t.Get<ReportCatalog>();
        Assert.Equal(40, catalog.Reports.Count);
        var company = await _t.Get<SettingsService>().GetAsync();
        long jobId, customerId, accountId;
        await using (var db = _t.Get<IAppDbFactory>().Create())
        {
            jobId = await db.Jobs.Where(j => j.InvoicedRevenue > 0).Select(j => j.Id).FirstAsync();
            customerId = await db.Customers.Select(c => c.Id).FirstAsync();
            accountId = await db.Accounts.Where(a => a.SystemKey == SystemAccounts.Bank).Select(a => a.Id).FirstAsync();
        }
        var today = DateTime.Today;
        var filter = new ReportFilter(new DateRange(today.AddYears(-1), today), today, CustomerId: customerId, JobId: jobId, AccountId: accountId);
        var dir = Path.Combine(_t.Folder, "reports-" + lang);
        Directory.CreateDirectory(dir);
        var withRows = 0;
        foreach (var r in catalog.Reports)
        {
            var table = await catalog.RunAsync(r.Key, filter);
            Assert.False(string.IsNullOrWhiteSpace(table.Title), r.Key);
            if (table.Rows.Count > 0) withRows++;
            var pdf = PdfExporter.Render(table, company);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
            var xlsx = Path.Combine(dir, r.Key + ".xlsx");
            ExcelExporter.Export(table, company, xlsx);
            using (var wb = new XLWorkbook(xlsx)) Assert.NotNull(wb.Worksheet(1));
            var csv = Path.Combine(dir, r.Key + ".csv");
            CsvExporter.Export(table, csv);
            Assert.True(new FileInfo(csv).Length > 3);
        }
        _out.WriteLine($"{withRows} of {catalog.Reports.Count} reports returned rows");
        Assert.True(withRows >= 36, $"only {withRows} reports returned data on the demo company");

        // documents
        await using (var db = _t.Get<IAppDbFactory>().Create())
        {
            var qId = await db.Quotations.Select(q => q.Id).FirstAsync();
            var iId = await db.SalesInvoices.Select(i => i.Id).FirstAsync();
            var q = await _t.Get<QuotationService>().GetAsync(qId);
            var inv = await _t.Get<SalesService>().GetInvoiceAsync(iId);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(DocumentRenderer.Quotation(q!, company), 0, 4));
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(DocumentRenderer.Invoice(inv!, company), 0, 4));
        }
    }
}

public class LocalizationTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", ".."));

    [Fact]
    public void Arabic_and_English_have_the_same_keys_and_placeholders()
    {
        var ar = Loc.Instance.Strings("ar");
        var en = Loc.Instance.Strings("en");
        Assert.True(en.Count > 1000);
        Assert.Empty(en.Keys.Except(ar.Keys));
        Assert.Empty(ar.Keys.Except(en.Keys));
        var ph = new Regex(@"\{\d+\}");
        foreach (var k in en.Keys)
            Assert.True(ph.Matches(en[k]).Select(m => m.Value).OrderBy(x => x).SequenceEqual(ph.Matches(ar[k]).Select(m => m.Value).OrderBy(x => x)), $"placeholders differ: {k}");
        var arabicLetters = new Regex(@"[؀-ۿ]");
        var untranslated = ar.Where(p => !arabicLetters.IsMatch(p.Value) && !p.Key.StartsWith("Lang.")).Select(p => p.Key).ToList();
        Assert.True(untranslated.Count == 0, "Arabic strings without Arabic text: " + string.Join(", ", untranslated));
    }

    [Fact]
    public void Every_enum_value_has_a_translation()
    {
        var enums = typeof(LaserWorks.Domain.Enums.JobStatus).Assembly.GetTypes().Where(t => t.IsEnum && t.Namespace == "LaserWorks.Domain.Enums").ToList();
        var missing = new List<string>();
        foreach (var e in enums)
            foreach (var name in Enum.GetNames(e).Where(n => n != "None" && n != "All"))
                if (!Loc.Instance.Strings("en").ContainsKey($"Enum.{e.Name}.{name}")) missing.Add($"Enum.{e.Name}.{name}");
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_key_referenced_in_code_and_xaml_exists()
    {
        var src = Path.Combine(Root(), "src");
        var literal = new Regex("\"((?:[A-Z][A-Za-z0-9]*)\\.(?:[A-Za-z0-9_]+)(?:\\.[A-Za-z0-9_]+)*)\"");
        var markup = new Regex(@"\{l:T ([A-Za-z0-9_.]+)\}");
        var prefixes = Loc.Instance.Strings("en").Keys.Select(k => k.Split('.')[0]).ToHashSet();
        var missing = new SortedSet<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("Migrations")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in markup.Matches(text)) if (!Loc.Instance.Has(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
            foreach (Match m in literal.Matches(text))
            {
                var key = m.Groups[1].Value;
                if (!prefixes.Contains(key.Split('.')[0])) continue; // not a resource-key namespace (types, file names, selectors)
                if (Regex.IsMatch(key, @"\.(json|ttf|png|ico|db|log|axaml|xlsx|pdf|csv|lwbak|txt|flag)$", RegexOptions.IgnoreCase)) continue;
                if (key.Count(c => c == '.') >= 1 && key.StartsWith("Enum.") && key.Split('.').Length < 3) continue;
                if (!Loc.Instance.Has(key)) missing.Add(key);
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Formatting_keeps_signs_in_place_for_right_to_left_text()
    {
        Assert.StartsWith(Loc.Lrm, Loc.Instance.Money(-12.5m));
        Assert.Contains("-12.50", Loc.Instance.Money(-12.5m));
        Assert.Equal("2026-03-01", Loc.Instance.Date(new DateTime(2026, 3, 1)));
    }
}
