using System.Globalization;
using Offload.Crm.Application.Common;
using Offload.Crm.Web.Ui;

namespace Offload.Crm.Web.Api;

/// <summary>
/// Query-string reader for API list endpoints (D-25, BUG-025). Names are the same as the UI URL keys
/// (<see cref="QueryNames"/>); aliases such as <c>statusId</c> or <c>size</c> are accepted. Unknown parameters and
/// unparsable values are rejected with 400 field errors instead of being silently ignored.
/// </summary>
public sealed class ApiQuery
{
    public static readonly string[] PagingNames = [QueryNames.Search, QueryNames.Sort, QueryNames.Descending, QueryNames.Page, QueryNames.PageSize];

    private readonly Dictionary<string, List<string>> _values;

    private ApiQuery(Dictionary<string, List<string>> values) => _values = values;

    /// <summary>Parses the request query; <paramref name="filters"/> are the endpoint's allowed filter names.</summary>
    public static ApiQuery Parse(HttpRequest request, bool paged, params string[] filters)
    {
        var allowed = new HashSet<string>(filters, StringComparer.OrdinalIgnoreCase);
        if (paged)
        {
            allowed.UnionWith(PagingNames);
        }

        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string[]>();
        foreach (var (key, raw) in request.Query)
        {
            var name = QueryNames.Canonical(key);
            if (!allowed.Contains(name))
            {
                errors[key] = [$"Unknown query parameter. Allowed: {string.Join(", ", allowed.Order(StringComparer.OrdinalIgnoreCase))}."];
                continue;
            }

            if (!values.TryGetValue(name, out var list))
            {
                values[name] = list = [];
            }

            // Multi-values may be repeated (?status=1&status=2) or comma-separated (?status=1,2).
            list.AddRange(raw.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        return new ApiQuery(values);
    }

    public string? String(string name) => _values.TryGetValue(name, out var v) && v.Count > 0 ? string.Join(',', v) : null;

    public IReadOnlyList<string> Strings(string name) => _values.TryGetValue(name, out var v) ? v : [];

    public int? Int(string name)
    {
        var value = String(name);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : throw Invalid(name, value, "a whole number");
    }

    public IReadOnlyList<int> Ints(string name) =>
        Strings(name).Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : throw Invalid(name, s, "whole numbers")).ToList();

    public bool? Bool(string name)
    {
        var value = String(name);
        if (value is null)
        {
            return null;
        }

        return bool.TryParse(value, out var b) ? b : throw Invalid(name, value, "true or false");
    }

    public DateOnly? Date(string name)
    {
        var value = String(name);
        if (value is null)
        {
            return null;
        }

        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw Invalid(name, value, "a date in the format yyyy-MM-dd");
    }

    /// <summary>An enum by name (case-insensitive) or number; undefined values are rejected (D-20.4).</summary>
    public TEnum? Enum<TEnum>(string name)
        where TEnum : struct, Enum
    {
        var value = String(name);
        return value is null ? null : ParseEnum<TEnum>(name, value);
    }

    public IReadOnlyList<TEnum> Enums<TEnum>(string name)
        where TEnum : struct, Enum => Strings(name).Select(s => ParseEnum<TEnum>(name, s)).ToList();

    /// <summary>Copies <c>search, sort, descending, page, pageSize</c> (clamped by the service, D-20.2).</summary>
    public T Paged<T>(T query)
        where T : PagedQuery
    {
        query.Search = String(QueryNames.Search);
        query.Sort = String(QueryNames.Sort);
        query.Descending = Bool(QueryNames.Descending) ?? false;
        query.Page = Int(QueryNames.Page) ?? 1;
        query.PageSize = Int(QueryNames.PageSize) ?? PagedQuery.DefaultPageSize;
        return query;
    }

    private static TEnum ParseEnum<TEnum>(string name, string value)
        where TEnum : struct, Enum
    {
        if (System.Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && System.Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new AppValidationException(name, $"'{value}' is not a valid value. Allowed: {string.Join(", ", System.Enum.GetNames<TEnum>())}.");
    }

    private static AppValidationException Invalid(string name, string value, string expected) =>
        new(name, $"'{value}' is not valid; expected {expected}.");
}

/// <summary>Endpoint metadata: the query parameters of a list endpoint, for the OpenAPI document.</summary>
public sealed record ApiQueryParameters(bool Paged, IReadOnlyList<(string Name, string Description)> Filters);
