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

    /// <summary>
    /// Sorts the entity query by a property path such as "Date" or "Customer.Name" (case-insensitive).
    /// Unknown paths fall back to the default key. Sorting happens before projection so it is translated to SQL.
    /// </summary>
    public static IQueryable<T> SortBy<T, TKey>(this IQueryable<T> query, string? path, bool desc, Expression<Func<T, TKey>> defaultKey, bool defaultDesc = false)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var param = Expression.Parameter(typeof(T), "x");
            Expression body = param;
            var ok = true;
            foreach (var part in path.Split('.'))
            {
                var prop = body.Type.GetProperties().FirstOrDefault(p => string.Equals(p.Name, part, StringComparison.OrdinalIgnoreCase));
                if (prop == null || prop.GetIndexParameters().Length > 0 || prop.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute), true).Length > 0) { ok = false; break; }
                body = Expression.Property(body, prop);
            }
            if (ok && body != param && (body.Type.IsPrimitive || body.Type.IsEnum || body.Type == typeof(string) || body.Type == typeof(decimal) || body.Type == typeof(DateTime)
                                        || Nullable.GetUnderlyingType(body.Type) != null))
            {
                var lambda = Expression.Lambda(body, param);
                var call = Expression.Call(typeof(Queryable), desc ? "OrderByDescending" : "OrderBy", new[] { typeof(T), body.Type }, query.Expression, Expression.Quote(lambda));
                return query.Provider.CreateQuery<T>(call);
            }
        }
        return defaultDesc ? query.OrderByDescending(defaultKey) : query.OrderBy(defaultKey);
    }

    public static string? Norm(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
