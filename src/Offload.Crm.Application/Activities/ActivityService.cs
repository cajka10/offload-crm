using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Activities;

public static class ActivitySort
{
    public const string Date = "date";
    public const string Type = "type";
    public const string Customer = "customer";
    public const string Project = "project";
    public const string Author = "author";
    public const string Created = "created";
}

public sealed class ActivityListQuery : PagedQuery
{
    public ActivityType? Type { get; set; }

    /// <summary>The customer's own activities and those of its projects (D-06).</summary>
    public int? CustomerId { get; set; }

    public int? ProjectId { get; set; }

    public string? AuthorId { get; set; }

    /// <summary>Local (Europe/Bratislava) date range, inclusive.</summary>
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}

public sealed record ActivityDto(
    int Id,
    ActivityType Type,
    DateTime Date,
    string? Subject,
    string Description,
    int? CustomerId,
    string? CustomerName,
    int? ProjectId,
    string? ProjectName,
    string AuthorId,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Create/update payload (D-12). The author is always the current user and can't be set.</summary>
public sealed class ActivityInput : INormalizable
{
    [EnumDefined(ErrorMessage = "Select a valid type.")]
    public ActivityType Type { get; set; } = ActivityType.Note;

    /// <summary>
    /// Defaults to now. A value with an offset or "Z" is an exact instant; a value without one (e.g.
    /// <c>2026-09-28T10:00:00</c>) is Europe/Bratislava local time (D-11, BUG-026). Stored as UTC.
    /// </summary>
    [Required(ErrorMessage = "Date is required.")]
    [PlausibleDate]
    public DateTime? Date { get; set; }

    [StringLength(200, ErrorMessage = "Subject can be at most 200 characters.")]
    public string? Subject { get; set; }

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(4000, ErrorMessage = "Description can be at most 4000 characters.")]
    public string? Description { get; set; }

    public int? CustomerId { get; set; }

    public int? ProjectId { get; set; }

    public void Normalize()
    {
        Subject = InputText.Clean(Subject);
        Description = InputText.Clean(Description);
        if (Date is { } d)
        {
            Date = d.Kind switch
            {
                DateTimeKind.Utc => d,
                DateTimeKind.Local => d.ToUniversalTime(),
                _ => AppTime.LocalToUtc(d),
            };
        }
    }

    public static ActivityInput From(ActivityDto a) => new()
    {
        Type = a.Type,
        Date = a.Date,
        Subject = a.Subject,
        Description = a.Description,
        CustomerId = a.CustomerId,
        ProjectId = a.ProjectId,
    };
}

public interface IActivityService
{
    /// <summary>Filters, sort (<see cref="ActivitySort"/>, default newest first) and paging.</summary>
    Task<PagedResult<ActivityDto>> ListAsync(ActivityListQuery query, CancellationToken ct = default);

    Task<ActivityDto> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Creates with the current user as author.</summary>
    Task<ActivityDto> CreateAsync(ActivityInput input, CancellationToken ct = default);

    /// <summary>Updates; the author never changes.</summary>
    Task<ActivityDto> UpdateAsync(int id, ActivityInput input, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class ActivityService(IAppDbContextFactory dbFactory, ICurrentUser currentUser, TimeProvider clock) : IActivityService
{
    public const string NoTargetMessage = "Select a customer or a project.";
    public const string MismatchMessage = "The selected project belongs to a different customer.";
    public const string FutureMessage = "The date can be at most one year in the future.";

    private static readonly SortMap<Activity> Sorts = new SortMap<Activity>(ActivitySort.Date, a => a.Id)
        .Add(ActivitySort.Date, a => a.Date)
        .AddEnum(ActivitySort.Type, a => a.Type)
        .Add(ActivitySort.Customer, a => a.Customer != null ? a.Customer.CompanyName : null)
        .Add(ActivitySort.Project, a => a.Project != null ? a.Project.Name : null)
        .Add(ActivitySort.Author, a => a.Author.FullName)
        .Add(ActivitySort.Created, a => a.CreatedAt);

    private static readonly Expression<Func<Activity, ActivityDto>> ToDto = a => new ActivityDto(
        a.Id,
        a.Type,
        a.Date,
        a.Subject,
        a.Description,
        a.CustomerId,
        a.Customer != null ? a.Customer.CompanyName : null,
        a.ProjectId,
        a.Project != null ? a.Project.Name : null,
        a.AuthorId,
        a.Author.FullName,
        a.CreatedAt,
        a.UpdatedAt);

    public async Task<PagedResult<ActivityDto>> ListAsync(ActivityListQuery query, CancellationToken ct = default)
    {
        Guard.EnumDefined(query.Type, "type");

        await using var db = await dbFactory.CreateAsync(ct);
        IQueryable<Activity> activities = db.Activities.AsNoTracking();

        if (query.Type is { } type)
        {
            activities = activities.Where(a => a.Type == type);
        }

        if (query.CustomerId is { } customerId)
        {
            activities = activities.Where(a => a.CustomerId == customerId || (a.ProjectId != null && a.Project!.CustomerId == customerId));
        }

        if (query.ProjectId is { } projectId)
        {
            activities = activities.Where(a => a.ProjectId == projectId);
        }

        if (InputText.Clean(query.AuthorId) is { } authorId)
        {
            activities = activities.Where(a => a.AuthorId == authorId);
        }

        if (query.From is { } from)
        {
            var fromUtc = AppTime.StartOfDayUtc(from);
            activities = activities.Where(a => a.Date >= fromUtc);
        }

        if (query.To is { } to && to < DateOnly.MaxValue)
        {
            var toUtc = AppTime.StartOfDayUtc(to.AddDays(1));
            activities = activities.Where(a => a.Date < toUtc);
        }

        if (query.NormalizedSearch is { } term)
        {
            activities = activities.Where(a => a.Description.ToLower().Contains(term) || (a.Subject != null && a.Subject.ToLower().Contains(term)));
        }

        // Newest first unless the caller asks for something else (T-19).
        var descending = query.Sort is null || query.Descending;
        return await Sorts.Apply(activities, query.Sort, descending).ToPagedResultAsync(query, q => q.Select(ToDto), ct);
    }

    public async Task<ActivityDto> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        return await GetDtoAsync(db, id, ct);
    }

    public async Task<ActivityDto> CreateAsync(ActivityInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var authorId = await currentUser.GetUserIdAsync(ct) ?? throw new SessionExpiredException();
        var customerId = await ValidateAsync(db, input, ct);

        var activity = new Activity { AuthorId = authorId };
        Apply(activity, input, customerId);
        db.Activities.Add(activity);
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(db, activity.Id, ct);
    }

    public async Task<ActivityDto> UpdateAsync(int id, ActivityInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Activity", id);
        var customerId = await ValidateAsync(db, input, ct);

        Apply(activity, input, customerId);
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(db, id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Activity", id);
        db.Activities.Remove(activity);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Validates D-12 rules and returns the customer id to store (the project's customer when a project is set).</summary>
    private async Task<int?> ValidateAsync(IAppDbContext db, ActivityInput input, CancellationToken ct)
    {
        input.Date ??= clock.GetUtcNow().UtcDateTime;
        input.Normalize();
        var errors = ModelValidator.Validate(input);

        if (input.Date > clock.GetUtcNow().UtcDateTime.AddYears(1))
        {
            errors[nameof(ActivityInput.Date)] = [FutureMessage];
        }

        int? customerId = input.CustomerId;
        if (input.CustomerId is null && input.ProjectId is null)
        {
            errors[nameof(ActivityInput.CustomerId)] = [NoTargetMessage];
        }
        else if (input.ProjectId is { } projectId)
        {
            var projectCustomer = await db.Projects.Where(p => p.Id == projectId).Select(p => (int?)p.CustomerId).FirstOrDefaultAsync(ct);
            if (projectCustomer is null)
            {
                errors[nameof(ActivityInput.ProjectId)] = ["Select an existing project."];
            }
            else if (input.CustomerId is { } requested && requested != projectCustomer)
            {
                errors[nameof(ActivityInput.ProjectId)] = [MismatchMessage];
            }
            else
            {
                customerId = projectCustomer; // the project implies its customer
            }
        }
        else if (!await db.Customers.AnyAsync(c => c.Id == input.CustomerId, ct))
        {
            errors[nameof(ActivityInput.CustomerId)] = ["Select an existing customer."];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        return customerId;
    }

    private static void Apply(Activity activity, ActivityInput input, int? customerId)
    {
        activity.Type = input.Type;
        activity.Date = input.Date!.Value;
        activity.Subject = input.Subject;
        activity.Description = input.Description!;
        activity.ProjectId = input.ProjectId;
        activity.CustomerId = customerId;
    }

    private static async Task<ActivityDto> GetDtoAsync(IAppDbContext db, int id, CancellationToken ct) =>
        await db.Activities.AsNoTracking().Where(a => a.Id == id).Select(ToDto).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Activity", id);
}
