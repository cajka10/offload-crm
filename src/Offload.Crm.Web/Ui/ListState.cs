using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Offload.Crm.Application.Common;

namespace Offload.Crm.Web.Ui;

/// <summary>
/// The state of a list page, kept in the URL query string (D-20.1). The parameter names are the same as the API's
/// (D-25, BUG-025): <c>search, sort, descending, page, pageSize</c> plus entity filters. Older/alternative names are
/// accepted as aliases (<see cref="QueryNames.Aliases"/>) and rewritten to the canonical names. Defaults are omitted.
/// </summary>
public sealed class ListState
{
    public static readonly int[] PageSizes = [25, 50, 100];

    public string? Search { get; set; }

    /// <summary>Null = the page's default sort.</summary>
    public string? Sort { get; set; }

    public bool Descending { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = PagedQuery.DefaultPageSize;

    public Dictionary<string, string?> Filters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Canonical, order-independent representation (used to detect no-op changes and our own navigations).</summary>
    public string Key => string.Join(
        "&",
        ToQuery()
            .Where(p => p.Value is not null)
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Key.ToLowerInvariant()}={p.Value}"));

    public string? Filter(string key) => Filters.TryGetValue(key, out var value) ? value : null;

    public int? IntFilter(string key) => int.TryParse(Filter(key), out var value) ? value : null;

    public static ListState FromUri(string uri, IEnumerable<string> filterKeys)
    {
        var raw = QueryHelpers.ParseQuery(new Uri(uri).Query);
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            var text = string.Join(',', value.Where(v => !string.IsNullOrWhiteSpace(v)));
            if (text.Length > 0)
            {
                query[QueryNames.Canonical(key)] = text; // aliases (size, desc, statusId, …) map to canonical names
            }
        }

        string? Get(string key) => query.TryGetValue(key, out var v) ? v : null;

        var state = new ListState
        {
            Search = Get(QueryNames.Search),
            Sort = Get(QueryNames.Sort),
            Descending = string.Equals(Get(QueryNames.Descending), "true", StringComparison.OrdinalIgnoreCase),
            Page = int.TryParse(Get(QueryNames.Page), out var page) && page > 0 ? page : 1,
            PageSize = int.TryParse(Get(QueryNames.PageSize), out var size) ? Math.Clamp(size, 1, PagedQuery.MaxPageSize) : PagedQuery.DefaultPageSize,
        };
        foreach (var key in filterKeys)
        {
            state.Filters[key] = Get(key);
        }

        return state;
    }

    public ListState Clone()
    {
        var clone = new ListState { Search = Search, Sort = Sort, Descending = Descending, Page = Page, PageSize = PageSize };
        foreach (var (key, value) in Filters)
        {
            clone.Filters[key] = value;
        }

        return clone;
    }

    /// <summary>Query parameters for <c>NavigationManager.GetUriWithQueryParameters</c>; null removes a parameter (incl. aliases).</summary>
    public IReadOnlyDictionary<string, object?> ToQuery()
    {
        var query = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in QueryNames.Aliases.Keys)
        {
            query[alias] = null; // canonicalize: drop alias spellings from the URL
        }

        query[QueryNames.Search] = string.IsNullOrWhiteSpace(Search) ? null : Search;
        query[QueryNames.Sort] = Sort;
        query[QueryNames.Descending] = Sort is not null && Descending ? "true" : null;
        query[QueryNames.Page] = Page > 1 ? Page.ToString(CultureInfo.InvariantCulture) : null;
        query[QueryNames.PageSize] = PageSize != PagedQuery.DefaultPageSize ? PageSize.ToString(CultureInfo.InvariantCulture) : null;
        foreach (var (key, value) in Filters)
        {
            query[key] = string.IsNullOrEmpty(value) ? null : value;
        }

        return query;
    }
}
