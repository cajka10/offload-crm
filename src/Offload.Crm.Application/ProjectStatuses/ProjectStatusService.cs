using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Entities;

namespace Offload.Crm.Application.ProjectStatuses;

public sealed record ProjectStatusDto(
    int Id,
    string Name,
    string Color,
    int SortOrder,
    bool IsActive,
    bool IsClosed,
    bool IsDefault,
    int ProjectCount);

/// <summary>Create/update payload (D-02, D-12).</summary>
public sealed class ProjectStatusInput : INormalizable
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50, ErrorMessage = "Name can be at most 50 characters.")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Color is required.")]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Enter a hex color like #0F5132.")]
    public string? Color { get; set; } = "#6C757D";

    /// <summary>Projects in this status count as active (D-03).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Terminal status (Delivered, Cancelled…).</summary>
    public bool IsClosed { get; set; }

    /// <summary>Make this the default for new projects (the previous default is unset).</summary>
    public bool IsDefault { get; set; }

    public void Normalize()
    {
        Name = InputText.Clean(Name);
        Color = InputText.Clean(Color)?.ToUpperInvariant();
    }

    public static ProjectStatusInput From(ProjectStatusDto s) => new()
    {
        Name = s.Name,
        Color = s.Color,
        IsActive = s.IsActive,
        IsClosed = s.IsClosed,
        IsDefault = s.IsDefault,
    };
}

public interface IProjectStatusService
{
    /// <summary>All statuses in <c>SortOrder</c> (the order used by every status dropdown).</summary>
    Task<IReadOnlyList<ProjectStatusDto>> ListAsync(CancellationToken ct = default);

    Task<ProjectStatusDto> GetAsync(int id, CancellationToken ct = default);

    Task<ProjectStatusDto> CreateAsync(ProjectStatusInput input, CancellationToken ct = default);

    Task<ProjectStatusDto> UpdateAsync(int id, ProjectStatusInput input, CancellationToken ct = default);

    /// <summary>Blocked (409) while projects use it or when it is the default (D-02).</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Swaps the sort order with the previous (<paramref name="up"/>) or next status.</summary>
    Task MoveAsync(int id, bool up, CancellationToken ct = default);

    Task SetDefaultAsync(int id, CancellationToken ct = default);
}

public sealed class ProjectStatusService(IAppDbContextFactory dbFactory) : IProjectStatusService
{
    public const string BothFlagsMessage = "A status can't be both active and closed.";
    public const string DuplicateNameMessage = "A status with this name already exists.";
    public const string ClosedDefaultMessage = "The default status must be an open (non-closed) status.";
    public const string InactiveDefaultMessage = "The default status must be an active status.";

    public async Task<IReadOnlyList<ProjectStatusDto>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        return await Project(db.ProjectStatuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id)).ToListAsync(ct);
    }

    public async Task<ProjectStatusDto> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        return await GetDtoAsync(db, id, ct);
    }

    public async Task<ProjectStatusDto> CreateAsync(ProjectStatusInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        await ValidateAsync(db, input, null, ct);

        var maxOrder = await db.ProjectStatuses.MaxAsync(s => (int?)s.SortOrder, ct) ?? 0;
        var status = new ProjectStatus { SortOrder = maxOrder + 1 };
        Apply(status, input);

        await using var tx = await db.BeginTransactionAsync(ct);
        if (input.IsDefault)
        {
            await UnsetDefaultAsync(db, ct);
        }

        status.IsDefault = input.IsDefault;
        db.ProjectStatuses.Add(status);
        await SaveAsync(db, input, null, ct);
        await tx.CommitAsync(ct);

        return await GetDtoAsync(db, status.Id, ct);
    }

    public async Task<ProjectStatusDto> UpdateAsync(int id, ProjectStatusInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var status = await db.ProjectStatuses.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Project status", id);

        await ValidateAsync(db, input, id, ct);
        if (status.IsDefault && !input.IsDefault)
        {
            throw new AppValidationException(nameof(ProjectStatusInput.IsDefault), "Set another status as the default instead.");
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        if (input.IsDefault && !status.IsDefault)
        {
            await UnsetDefaultAsync(db, ct);
        }

        Apply(status, input);
        status.IsDefault = input.IsDefault;
        await SaveAsync(db, input, id, ct);
        await tx.CommitAsync(ct);

        return await GetDtoAsync(db, id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var status = await db.ProjectStatuses.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Project status", id);

        if (status.IsDefault)
        {
            throw new ConflictException($"\"{status.Name}\" is the default status and can't be deleted. Set another default first.");
        }

        var used = await db.Projects.CountAsync(p => p.StatusId == id, ct);
        if (used > 0)
        {
            var noun = used == 1 ? "project uses" : "projects use";
            throw new ConflictException($"\"{status.Name}\" can't be deleted: {used} {noun} it. Move them to another status first.");
        }

        db.ProjectStatuses.Remove(status);
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveAsync(int id, bool up, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var all = await db.ProjectStatuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        var index = all.FindIndex(s => s.Id == id);
        if (index < 0)
        {
            throw new NotFoundException("Project status", id);
        }

        var target = up ? index - 1 : index + 1;
        if (target < 0 || target >= all.Count)
        {
            return; // already first/last
        }

        (all[index], all[target]) = (all[target], all[index]);
        for (var i = 0; i < all.Count; i++)
        {
            all[i].SortOrder = i + 1; // also repairs gaps/duplicates
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SetDefaultAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var status = await db.ProjectStatuses.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Project status", id);
        if (status.IsDefault)
        {
            return;
        }

        if (status.IsClosed)
        {
            throw new ConflictException(ClosedDefaultMessage);
        }

        if (!status.IsActive)
        {
            throw new ConflictException(InactiveDefaultMessage); // BUG-030
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        await UnsetDefaultAsync(db, ct);
        status.IsDefault = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>The default status for new projects (falls back to the first one if none is flagged).</summary>
    internal static async Task<ProjectStatus> GetDefaultAsync(IAppDbContext db, CancellationToken ct) =>
        await db.ProjectStatuses.OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder).FirstOrDefaultAsync(ct)
        ?? throw new ConflictException("No project statuses are configured.");

    private static async Task UnsetDefaultAsync(IAppDbContext db, CancellationToken ct)
    {
        // Saved separately: the filtered unique index allows only one default at any time.
        var current = await db.ProjectStatuses.Where(s => s.IsDefault).ToListAsync(ct);
        current.ForEach(s => s.IsDefault = false);
        await db.SaveChangesAsync(ct);
    }

    private static async Task ValidateAsync(IAppDbContext db, ProjectStatusInput input, int? currentId, CancellationToken ct)
    {
        input.Normalize();
        var errors = ModelValidator.Validate(input);

        if (input.IsActive && input.IsClosed)
        {
            errors[nameof(ProjectStatusInput.IsClosed)] = [BothFlagsMessage];
        }
        else if (input.IsDefault && input.IsClosed)
        {
            // BUG-020: new projects would start closed and vanish from the default Projects list.
            errors[nameof(ProjectStatusInput.IsClosed)] = [ClosedDefaultMessage];
        }
        else if (input.IsDefault && !input.IsActive)
        {
            // BUG-030: same as BUG-020 for inactive statuses (also when the current default is edited to inactive).
            errors[nameof(ProjectStatusInput.IsActive)] = [InactiveDefaultMessage];
        }

        if (!errors.ContainsKey(nameof(ProjectStatusInput.Name)) && await IsNameTakenAsync(db, input.Name, currentId, ct))
        {
            errors[nameof(ProjectStatusInput.Name)] = [DuplicateNameMessage];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }

    private static Task<bool> IsNameTakenAsync(IAppDbContext db, string? name, int? currentId, CancellationToken ct)
    {
        var lower = name!.ToLower();
        return db.ProjectStatuses.AnyAsync(s => s.Name.ToLower() == lower && (currentId == null || s.Id != currentId), ct);
    }

    private static async Task SaveAsync(IAppDbContext db, ProjectStatusInput input, int? currentId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (input.Name is not null)
        {
            if (await IsNameTakenAsync(db, input.Name, currentId, ct))
            {
                throw new AppValidationException(nameof(ProjectStatusInput.Name), DuplicateNameMessage);
            }

            throw;
        }
    }

    private static void Apply(ProjectStatus status, ProjectStatusInput input)
    {
        status.Name = input.Name!;
        status.Color = input.Color!;
        status.IsActive = input.IsActive;
        status.IsClosed = input.IsClosed;
    }

    private static async Task<ProjectStatusDto> GetDtoAsync(IAppDbContext db, int id, CancellationToken ct) =>
        await Project(db.ProjectStatuses.AsNoTracking().Where(s => s.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Project status", id);

    private static IQueryable<ProjectStatusDto> Project(IQueryable<ProjectStatus> q) =>
        q.Select(s => new ProjectStatusDto(s.Id, s.Name, s.Color, s.SortOrder, s.IsActive, s.IsClosed, s.IsDefault, s.Projects.Count));
}
