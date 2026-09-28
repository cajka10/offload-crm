using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Offload.Crm.Application.Common;
using Offload.Crm.Web.Ui;

namespace Offload.Crm.Web.Components.Shared;

/// <summary>
/// Base class of every list page (D-20). The URL is the single source of truth: user actions change the query
/// string, and every URL change (re)loads the data. Handles loading/error/empty/no-results states and jumps back to
/// the last existing page when the current one became empty (BUG-005).
/// </summary>
public abstract class ListPageBase<TItem> : ComponentBase, IDisposable
{
    private string _path = string.Empty;
    private string? _loadedUri;
    private string? _pushedSearch;
    private CancellationTokenSource? _cts;
    private ListState? _desired;
    private readonly HashSet<string> _pushedKeys = [];

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    [Inject]
    protected ILoggerFactory LoggerFactory { get; set; } = default!;

    [Inject]
    protected ListLocations ListLocations { get; set; } = default!;

    protected ListState State { get; private set; } = new();

    protected IReadOnlyList<TItem> Items { get; private set; } = [];

    protected int TotalCount { get; private set; }

    protected bool Loading { get; private set; } = true;

    protected bool LoadError { get; private set; }

    /// <summary>True when nothing exists at all (not just nothing matching the filters).</summary>
    protected bool IsEmptyDatabase { get; private set; }

    /// <summary>The text shown in the search box (kept separate so typing isn't overwritten by URL round-trips).</summary>
    protected string? SearchText { get; set; }

    /// <summary>Query-string keys of the page's filters.</summary>
    protected virtual IEnumerable<string> FilterKeys => [];

    /// <summary>Sort key used when the URL has none.</summary>
    protected abstract string DefaultSort { get; }

    protected virtual bool DefaultDescending => false;

    protected string EffectiveSort => State.Sort ?? DefaultSort;

    protected bool EffectiveDescending => State.Sort is null ? DefaultDescending : State.Descending;

    protected abstract Task<PagedResult<TItem>> FetchAsync(ListState state, CancellationToken ct);

    /// <summary>Called only when a load returned nothing: does the table have any rows at all?</summary>
    protected abstract Task<bool> IsDatabaseEmptyAsync(CancellationToken ct);

    protected override async Task OnInitializedAsync()
    {
        _path = CurrentPath();
        Navigation.LocationChanged += OnLocationChanged;
        await LoadFromUrlAsync(force: true);
    }

    /// <summary>Hook to derive page fields (filter controls) from <see cref="State"/> after the URL was parsed.</summary>
    protected virtual void OnStateLoaded()
    {
    }

    protected Task ReloadAsync() => LoadFromUrlAsync(force: true);

    protected void SortBy(string key) => Change(s =>
    {
        var sameColumn = string.Equals(EffectiveSort, key, StringComparison.OrdinalIgnoreCase);
        s.Sort = key;
        s.Descending = sameColumn && !EffectiveDescending;
        s.Page = 1;
    });

    protected void GoToPage(int page) => Change(s => s.Page = Math.Max(1, page));

    protected void SetPageSize(int size) => Change(s =>
    {
        s.PageSize = size;
        s.Page = 1;
    });

    protected void SetSearch(string? text)
    {
        SearchText = text;
        _pushedSearch = string.IsNullOrWhiteSpace(text) ? null : text;
        Change(s =>
        {
            s.Search = _pushedSearch;
            s.Page = 1;
        });
    }

    protected void SetFilter(string key, string? value) => Change(s =>
    {
        s.Filters[key] = value;
        s.Page = 1;
    });

    /// <summary>Removes search and all filters (filters with defaults fall back to them).</summary>
    protected virtual void ClearFilters()
    {
        SearchText = null;
        _pushedSearch = null;
        Change(s =>
        {
            s.Search = null;
            foreach (var key in s.Filters.Keys.ToList())
            {
                s.Filters[key] = null;
            }

            s.Page = 1;
        });
    }

    /// <summary>
    /// Applies a change to the latest *desired* state (not the last loaded one, which may lag behind a navigation that
    /// is still in flight) and navigates, unless nothing actually changed (BUG-016).
    /// </summary>
    protected void Change(Action<ListState> change)
    {
        var current = _desired ?? State;
        var next = current.Clone();
        change(next);
        if (next.Key == current.Key)
        {
            return;
        }

        _desired = next;
        _pushedKeys.Add(next.Key);
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameters(next.ToQuery()), replace: true);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (CurrentPath() == _path)
        {
            _ = InvokeAsync(() => LoadFromUrlAsync(force: false));
        }
    }

    private async Task LoadFromUrlAsync(bool force)
    {
        var uri = Navigation.Uri;
        if (!force && uri == _loadedUri)
        {
            return;
        }

        _loadedUri = uri;
        State = ListState.FromUri(uri, FilterKeys);
        ListLocations.Remember(_path, "/" + Navigation.ToBaseRelativePath(uri));

        // Our own navigations (possibly several in flight) keep the desired state; anything else (nav link, Back,
        // typed URL) resets it to what the URL says.
        if (!_pushedKeys.Remove(State.Key))
        {
            _desired = null;
            _pushedKeys.Clear();
        }
        else if (_desired?.Key == State.Key)
        {
            _desired = null;
            _pushedKeys.Clear();
        }
        if (State.Search != _pushedSearch)
        {
            SearchText = State.Search;
            _pushedSearch = State.Search;
        }

        OnStateLoaded();

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Loading = true;
        LoadError = false;
        StateHasChanged();

        try
        {
            var result = await FetchAsync(State, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var lastPage = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)result.PageSize));
            if (result.Items.Count == 0 && result.TotalCount > 0 && State.Page > lastPage)
            {
                GoToPage(lastPage); // BUG-005: the page became empty (delete/filter) -> last existing page
                return;
            }

            Items = result.Items;
            TotalCount = result.TotalCount;
            IsEmptyDatabase = result.TotalCount == 0 && await IsDatabaseEmptyAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is not SessionExpiredException)
        {
            LoggerFactory.CreateLogger(GetType()).LogError(ex, "Loading list failed");
            LoadError = true;
            Items = [];
            TotalCount = 0;
        }
        finally
        {
            if (ReferenceEquals(cts, _cts))
            {
                Loading = false;
                StateHasChanged();
            }
        }
    }

    private string CurrentPath() => new Uri(Navigation.Uri).AbsolutePath;

    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
