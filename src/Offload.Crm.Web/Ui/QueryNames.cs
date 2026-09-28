namespace Offload.Crm.Web.Ui;

/// <summary>
/// Query-string names shared by list pages and the REST API (D-25, BUG-025). Aliases are accepted everywhere and
/// mean exactly the same as their canonical name.
/// </summary>
public static class QueryNames
{
    public const string Search = "search";
    public const string Sort = "sort";
    public const string Descending = "descending";
    public const string Page = "page";
    public const string PageSize = "pageSize";

    public const string Status = "status";
    public const string Customer = "customer";
    public const string Project = "project";
    public const string Priority = "priority";
    public const string Responsible = "responsible";
    public const string Assignee = "assignee";
    public const string Author = "author";
    public const string Active = "active";
    public const string Overdue = "overdue";
    public const string Type = "type";
    public const string From = "from";
    public const string To = "to";
    public const string View = "view";

    /// <summary>Alias → canonical name (older API names and short UI names).</summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["desc"] = Descending,
        ["size"] = PageSize,
        ["statusId"] = Status,
        ["customerId"] = Customer,
        ["projectId"] = Project,
        ["responsibleUserId"] = Responsible,
        ["authorId"] = Author,
        ["activeOnly"] = Active,
    };

    public static string Canonical(string name) => Aliases.TryGetValue(name, out var canonical) ? canonical : name;
}
