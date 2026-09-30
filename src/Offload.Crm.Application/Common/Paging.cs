using Microsoft.EntityFrameworkCore;

namespace Offload.Crm.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

/// <summary>Common list parameters (1-based page). Clamped per D-20.2.</summary>
public abstract class PagedQuery
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public string? Search { get; set; }

    /// <summary>Name of the column to sort by; each service documents the allowed values.</summary>
    public string? Sort { get; set; }

    public bool Descending { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize switch
    {
        < 1 => DefaultPageSize,
        > MaxPageSize => MaxPageSize,
        _ => PageSize,
    };

    public string? NormalizedSearch => InputText.SearchTerm(Search);
}

public static class PagingExtensions
{
    /// <summary>
    /// Counts, then fetches one page of an ordered query. The offset is computed in <see cref="long"/> and a page
    /// beyond the end returns an empty page without querying (no overflow, no negative OFFSET; BUG-002).
    /// </summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> ordered,
        PagedQuery query,
        Func<IQueryable<TSource>, IQueryable<TResult>> project,
        CancellationToken ct)
    {
        var page = query.NormalizedPage;
        var size = query.NormalizedPageSize;
        var total = await ordered.CountAsync(ct);

        var offset = (long)(page - 1) * size;
        if (offset >= total)
        {
            return new PagedResult<TResult>([], total, page, size);
        }

        var items = await project(ordered.Skip((int)offset).Take(size)).ToListAsync(ct);
        return new PagedResult<TResult>(items, total, page, size);
    }

    /// <summary>Same as the other overload when no projection is needed.</summary>
    public static Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> ordered, PagedQuery query, CancellationToken ct) =>
        ordered.ToPagedResultAsync(query, q => q, ct);
}

/// <summary>Id + display text for pickers/autocompletes.</summary>
public sealed record LookupItem(int Id, string Name);
