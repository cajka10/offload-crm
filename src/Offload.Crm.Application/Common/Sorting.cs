using System.Linq.Expressions;

namespace Offload.Crm.Application.Common;

/// <summary>
/// Sorting rules shared by all lists (D-20.3): nulls always last (both directions), enums in declaration
/// (lifecycle) order, and a stable tie-breaker.
/// </summary>
public static class SortingExtensions
{
    public static IOrderedQueryable<T> SortBy<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key, bool descending)
    {
        if (!IsNullable(typeof(TKey)))
        {
            return descending ? query.OrderByDescending(key) : query.OrderBy(key);
        }

        var nullsLast = query.OrderBy(IsNull(key)); // false (has value) sorts before true (null)
        return descending ? nullsLast.ThenByDescending(key) : nullsLast.ThenBy(key);
    }

    /// <summary>Sorts an enum column by declaration order instead of its (string) storage value.</summary>
    public static Expression<Func<T, int>> EnumOrder<T, TEnum>(Expression<Func<T, TEnum>> selector)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        Expression body = Expression.Constant(values.Length);
        for (var i = values.Length - 1; i >= 0; i--)
        {
            body = Expression.Condition(
                Expression.Equal(selector.Body, Expression.Constant(values[i], typeof(TEnum))),
                Expression.Constant(i),
                body);
        }

        return Expression.Lambda<Func<T, int>>(body, selector.Parameters);
    }

    private static bool IsNullable(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static Expression<Func<T, bool>> IsNull<T, TKey>(Expression<Func<T, TKey>> key) =>
        Expression.Lambda<Func<T, bool>>(Expression.Equal(key.Body, Expression.Constant(null, typeof(TKey))), key.Parameters);
}

/// <summary>Maps public sort keys (case-insensitive) to column expressions; unknown keys use the default.</summary>
public sealed class SortMap<T>
{
    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly Expression<Func<T, int>> _tieBreaker;
    private readonly string _defaultKey;

    public SortMap(string defaultKey, Expression<Func<T, int>> tieBreaker)
    {
        _defaultKey = defaultKey;
        _tieBreaker = tieBreaker;
    }

    public IReadOnlyCollection<string> Keys => _map.Keys;

    public SortMap<T> Add<TKey>(string key, Expression<Func<T, TKey>> selector)
    {
        _map[key] = (q, desc) => q.SortBy(selector, desc);
        return this;
    }

    public SortMap<T> AddEnum<TEnum>(string key, Expression<Func<T, TEnum>> selector)
        where TEnum : struct, Enum
    {
        var order = SortingExtensions.EnumOrder(selector);
        _map[key] = (q, desc) => desc ? q.OrderByDescending(order) : q.OrderBy(order);
        return this;
    }

    public IOrderedQueryable<T> Apply(IQueryable<T> query, string? sort, bool descending)
    {
        var apply = sort is not null && _map.TryGetValue(sort, out var fn) ? fn : _map[_defaultKey];
        return apply(query, descending).ThenBy(_tieBreaker);
    }
}
