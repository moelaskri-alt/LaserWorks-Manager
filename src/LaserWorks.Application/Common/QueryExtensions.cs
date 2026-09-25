using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace LaserWorks.Application.Common;

public static class QueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageRequest req, CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(req.Skip).Take(req.PageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, req.Page, req.PageSize);
    }

    /// <summary>Sorts by a property name (case-insensitive) when present, otherwise by the default key.</summary>
    public static IQueryable<T> SortBy<T, TKey>(this IQueryable<T> query, string? property, bool desc, Expression<Func<T, TKey>> defaultKey, bool defaultDesc = false)
    {
        if (!string.IsNullOrWhiteSpace(property))
        {
            var prop = typeof(T).GetProperties().FirstOrDefault(p => string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase));
            if (prop != null)
            {
                var param = Expression.Parameter(typeof(T), "x");
                var body = Expression.Property(param, prop);
                var lambda = Expression.Lambda(body, param);
                var method = desc ? "OrderByDescending" : "OrderBy";
                var call = Expression.Call(typeof(Queryable), method, new[] { typeof(T), prop.PropertyType }, query.Expression, Expression.Quote(lambda));
                return query.Provider.CreateQuery<T>(call);
            }
        }
        return defaultDesc ? query.OrderByDescending(defaultKey) : query.OrderBy(defaultKey);
    }

    public static string? Norm(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
