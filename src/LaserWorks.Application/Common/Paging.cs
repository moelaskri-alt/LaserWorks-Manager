namespace LaserWorks.Application.Common;

public sealed record PageRequest(string? Search = null, int Page = 1, int PageSize = 50, string? SortBy = null, bool Descending = false)
{
    public int Skip => Math.Max(0, (Page - 1) * PageSize);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record Lookup(long Id, string Code, string Name)
{
    public string Display => string.IsNullOrEmpty(Code) ? Name : $"{Code} - {Name}";
    public override string ToString() => Display;
}

public sealed record DateRange(DateTime From, DateTime To)
{
    public DateTime ToExclusive => To.Date.AddDays(1);
    public static DateRange ThisMonth(DateTime today) => new(new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1));
    public static DateRange ThisYear(DateTime today) => new(new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31));
}
