namespace Offload.Crm.Web.Ui;

/// <summary>
/// Remembers the last URL (with filters/sort/page) of each list page in this browser tab, so detail-page breadcrumbs
/// return to the same view (D-20.1, BUG-022). Scoped = one per circuit.
/// </summary>
public sealed class ListLocations
{
    private readonly Dictionary<string, string> _last = new(StringComparer.OrdinalIgnoreCase);

    public void Remember(string path, string relativeUri) => _last[path] = relativeUri;

    /// <summary>The remembered URL of the list at <paramref name="path"/> (e.g. "/customers"), or the path itself.</summary>
    public string For(string path) => _last.TryGetValue(path, out var uri) ? uri : path;
}
