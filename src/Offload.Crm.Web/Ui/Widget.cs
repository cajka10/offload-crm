namespace Offload.Crm.Web.Ui;

/// <summary>State of one dashboard widget: loads independently, so one failure doesn't break the page (D-23).</summary>
public sealed class Widget<T>
{
    private readonly Func<Task<T>> _load;
    private readonly ILogger _logger;

    public Widget(Func<Task<T>> load, ILogger logger)
    {
        _load = load;
        _logger = logger;
    }

    public T? Data { get; private set; }

    public bool Loading { get; private set; } = true;

    public bool Failed { get; private set; }

    public async Task LoadAsync()
    {
        Loading = true;
        Failed = false;
        try
        {
            Data = await _load();
        }
        catch (Exception ex) when (ex is not Offload.Crm.Application.Common.SessionExpiredException)
        {
            _logger.LogError(ex, "Dashboard widget {Widget} failed", typeof(T).Name);
            Failed = true;
        }
        finally
        {
            Loading = false;
        }
    }
}
