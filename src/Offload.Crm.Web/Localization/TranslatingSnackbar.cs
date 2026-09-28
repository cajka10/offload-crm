using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Offload.Crm.Web.Localization;

/// <summary>
/// D-29: translates every text snackbar in one place. Components keep passing English messages (including
/// service messages with values, e.g. "Customer has 3 projects…"); <see cref="Text.T(string?)"/> maps them.
/// </summary>
internal sealed class TranslatingSnackbar(ISnackbar inner) : ISnackbar
{
    public event Action? OnSnackbarsUpdated
    {
        add => inner.OnSnackbarsUpdated += value;
        remove => inner.OnSnackbarsUpdated -= value;
    }

    public IEnumerable<Snackbar> ShownSnackbars => inner.ShownSnackbars;

    public SnackbarConfiguration Configuration => inner.Configuration;

    public Snackbar? Add(string message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = null) =>
        inner.Add(Text.T(message), severity, configure, key);

    public Snackbar? Add(MarkupString message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = null) =>
        inner.Add(message, severity, configure, key);

    public Snackbar? Add(RenderFragment message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = null) =>
        inner.Add(message, severity, configure, key);

    public Snackbar? Add<T>(Dictionary<string, object>? componentParameters = null, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = "")
        where T : IComponent =>
        inner.Add<T>(componentParameters, severity, configure, key);

    public void Clear() => inner.Clear();

    public void Remove(Snackbar snackbar) => inner.Remove(snackbar);

    public void RemoveByKey(string key) => inner.RemoveByKey(key);

    public void Dispose() => inner.Dispose();

    /// <summary>Wraps MudBlazor's registered <see cref="ISnackbar"/> (call after <c>AddMudServices</c>).</summary>
    public static void Register(IServiceCollection services)
    {
        var original = services.Last(d => d.ServiceType == typeof(ISnackbar));
        services.Remove(original);
        services.Add(new ServiceDescriptor(
            typeof(ISnackbar),
            sp => new TranslatingSnackbar(original.ImplementationFactory is { } factory
                ? (ISnackbar)factory(sp)
                : (ISnackbar)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)),
            original.Lifetime));
    }
}
