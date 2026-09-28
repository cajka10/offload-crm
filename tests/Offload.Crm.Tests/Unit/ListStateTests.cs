using Offload.Crm.Web.Ui;

namespace Offload.Crm.Tests.Unit;

/// <summary>URL state of list pages (D-20.1, BUG-016, BUG-025).</summary>
public class ListStateTests
{
    private static readonly string[] Filters = [QueryNames.Status, QueryNames.Customer];

    [Fact]
    public void Aliases_map_to_canonical_names()
    {
        var state = ListState.FromUri("http://x/projects?statusId=1&statusId=3&customerId=7&desc=true&sort=name&size=50", Filters);

        Assert.Equal("1,3", state.Filter(QueryNames.Status));
        Assert.Equal(7, state.IntFilter(QueryNames.Customer));
        Assert.True(state.Descending);
        Assert.Equal(50, state.PageSize);
    }

    [Fact]
    public void Query_writes_canonical_names_and_removes_aliases_and_defaults()
    {
        var state = ListState.FromUri("http://x/projects?size=50&desc=true&sort=name&page=1", Filters);

        var query = state.ToQuery();

        Assert.Equal("50", query[QueryNames.PageSize]);
        Assert.Equal("true", query[QueryNames.Descending]);
        Assert.Null(query["size"]);
        Assert.Null(query["desc"]);
        Assert.Null(query[QueryNames.Page]); // page 1 is the default
    }

    [Fact]
    public void Key_is_the_same_for_equivalent_urls()
    {
        var a = ListState.FromUri("http://x/projects?status=1&customer=2&size=50", Filters);
        var b = ListState.FromUri("http://x/projects?customerId=2&pageSize=50&statusId=1", Filters);
        var c = ListState.FromUri("http://x/projects?status=2", Filters);

        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Key, c.Key);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var state = ListState.FromUri("http://x/projects?status=1", Filters);
        var clone = state.Clone();

        clone.Filters[QueryNames.Status] = "2";
        clone.Page = 3;

        Assert.Equal("1", state.Filter(QueryNames.Status));
        Assert.Equal(1, state.Page);
    }
}
