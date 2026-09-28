using Offload.Crm.Application.ProjectStatuses;

namespace Offload.Crm.Web.Auth;

/// <summary>D-27: everyone can read project statuses; only administrators can change them (UI and API).</summary>
internal sealed class AdminProjectStatusService(ProjectStatusService inner, AdminGuard guard) : IProjectStatusService
{
    private const string Action = "change project statuses";

    public Task<IReadOnlyList<ProjectStatusDto>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);

    public Task<ProjectStatusDto> GetAsync(int id, CancellationToken ct = default) => inner.GetAsync(id, ct);

    public async Task<ProjectStatusDto> CreateAsync(ProjectStatusInput input, CancellationToken ct = default)
    {
        await guard.EnsureAdminAsync(Action);
        return await inner.CreateAsync(input, ct);
    }

    public async Task<ProjectStatusDto> UpdateAsync(int id, ProjectStatusInput input, CancellationToken ct = default)
    {
        await guard.EnsureAdminAsync(Action);
        return await inner.UpdateAsync(id, input, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await guard.EnsureAdminAsync(Action);
        await inner.DeleteAsync(id, ct);
    }

    public async Task MoveAsync(int id, bool up, CancellationToken ct = default)
    {
        await guard.EnsureAdminAsync(Action);
        await inner.MoveAsync(id, up, ct);
    }

    public async Task SetDefaultAsync(int id, CancellationToken ct = default)
    {
        await guard.EnsureAdminAsync(Action);
        await inner.SetDefaultAsync(id, ct);
    }
}
