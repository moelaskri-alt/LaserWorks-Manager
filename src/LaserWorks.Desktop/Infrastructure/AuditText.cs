using System.Text.Json;
using LaserWorks.Domain.Enums;
using LaserWorks.Localization;

namespace LaserWorks.Desktop;

/// <summary>
/// Audit trail details as readable, translated text. A recorded change set ({"Status":"Draft → Final",...}) becomes
/// "الحالة: مسودة → نهائي · …": field names use their column labels where one exists, and status values
/// (job, estimate, quotation, request, revision, document, quality, operation, remnant, order, journal) are translated.
/// </summary>
public static class AuditText
{
    private static readonly Type[] StatusEnums =
    {
        typeof(JobStatus), typeof(EstimateStatus), typeof(QuotationStatus), typeof(RequestStatus), typeof(RevisionStatus), typeof(DocumentStatus),
        typeof(QualityStatus), typeof(OperationStatus), typeof(RemnantStatus), typeof(PurchaseOrderStatus), typeof(JournalStatus)
    };

    private static readonly Dictionary<string, object> Values = StatusEnums
        .SelectMany(t => Enum.GetValues(t).Cast<object>())
        .GroupBy(v => v.ToString()!).ToDictionary(g => g.Key, g => g.First());

    private static string Value(string v)
    {
        var t = v.Trim();
        return Values.TryGetValue(t, out var e) ? Loc.Instance.Enum(e) : t;
    }

    /// <summary>"A → B" with each side translated when it is a status name.</summary>
    private static string Change(string text) => string.Join(" → ", text.Split('→').Select(Value));

    private static string Field(string name) => Loc.Instance.Has("Col." + name) ? Loc.Instance["Col." + name] : name;

    public static string Format(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return "";
        if (details.TrimStart().StartsWith('{'))
        {
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string?>>(details);
                if (map != null) return string.Join(" · ", map.Select(p => $"{Field(p.Key)}: {Change(p.Value ?? "")}"));
            }
            catch (JsonException) { }
        }
        // free text such as "QT-00016 v1 → Sent" or "JOB-00007: Passed" — translate the status parts
        var colon = details.LastIndexOf(": ", StringComparison.Ordinal);
        return colon > 0 ? details[..(colon + 2)] + Change(details[(colon + 2)..]) : Change(details);
    }
}
