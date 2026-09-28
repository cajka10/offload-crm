using System.ComponentModel.DataAnnotations;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Projects;

/// <summary>Sort keys accepted by <see cref="IProjectService.ListAsync"/> (case-insensitive).</summary>
public static class ProjectSort
{
    public const string Name = "name";
    public const string Customer = "customer";
    public const string Status = "status";
    public const string Priority = "priority";
    public const string Responsible = "responsible";
    public const string Start = "start";
    public const string Target = "target";
    public const string EstimatedValue = "estimatedValue";
    public const string OpenTasks = "openTasks";
    public const string Created = "created";
}

public sealed class ProjectListQuery : PagedQuery
{
    /// <summary>Any of these statuses (empty/null = all).</summary>
    public IReadOnlyList<int>? StatusIds { get; set; }

    public int? CustomerId { get; set; }

    public Priority? Priority { get; set; }

    public string? ResponsibleUserId { get; set; }

    /// <summary>Only projects whose status counts as active (D-03). On by default (T-14); ignored when <see cref="StatusIds"/> are given (BUG-017).</summary>
    public bool ActiveOnly { get; set; } = true;
}

public sealed record ProjectListItem(
    int Id,
    string Name,
    int CustomerId,
    string CustomerName,
    int StatusId,
    string StatusName,
    string StatusColor,
    bool StatusIsClosed,
    Priority Priority,
    string? ResponsibleUserId,
    string? ResponsibleName,
    bool ResponsibleIsActive,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    decimal? EstimatedValue,
    int OpenTaskCount,
    DateTime CreatedAt)
{
    /// <summary>D-03: target date before today and the status isn't closed.</summary>
    public bool IsOverdue(DateOnly today) => TargetDate < today && !StatusIsClosed;
}

public sealed record ProjectDetail(
    int Id,
    string Name,
    string? Description,
    int CustomerId,
    string CustomerName,
    int StatusId,
    string StatusName,
    string StatusColor,
    bool StatusIsActive,
    bool StatusIsClosed,
    Priority Priority,
    string? ResponsibleUserId,
    string? ResponsibleName,
    bool ResponsibleIsActive,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    decimal? EstimatedValue,
    decimal? ActualValue,
    string? Notes,
    int TaskCount,
    int OpenTaskCount,
    int ActivityCount,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public bool IsOverdue(DateOnly today) => TargetDate < today && !StatusIsClosed;
}

/// <summary>A timeline entry (D-14): project created, activities and task completions.</summary>
public sealed record TimelineItem(
    DateTime At,
    TimelineKind Kind,
    string Title,
    string? Detail,
    string? AuthorName,
    int? ActivityId,
    int? TaskId,
    ActivityType? ActivityType);

public enum TimelineKind
{
    ProjectCreated,
    Activity,
    TaskCompleted,
}

/// <summary>Create/update payload (D-12).</summary>
public sealed class ProjectInput : INormalizable, IValidatableObject
{
    public const double MaxValue = 999_999_999_999;

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name can be at most 200 characters.")]
    public string? Name { get; set; }

    [StringLength(4000, ErrorMessage = "Description can be at most 4000 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Select a customer.")]
    public int? CustomerId { get; set; }

    /// <summary>Null = the default status (D-02).</summary>
    public int? StatusId { get; set; }

    [EnumDefined(ErrorMessage = "Select a valid priority.")]
    public Priority Priority { get; set; } = Priority.Medium;

    public string? ResponsibleUserId { get; set; }

    [PlausibleDate]
    public DateOnly? StartDate { get; set; }

    [PlausibleDate]
    public DateOnly? TargetDate { get; set; }

    [Range(0, MaxValue, ErrorMessage = "Enter a value between 0 and 999 999 999 999.")]
    [MaxDecimals(2)]
    public decimal? EstimatedValue { get; set; }

    [Range(0, MaxValue, ErrorMessage = "Enter a value between 0 and 999 999 999 999.")]
    [MaxDecimals(2)]
    public decimal? ActualValue { get; set; }

    [StringLength(4000, ErrorMessage = "Notes can be at most 4000 characters.")]
    public string? Notes { get; set; }

    public void Normalize()
    {
        Name = InputText.Clean(Name);
        Description = InputText.Clean(Description);
        ResponsibleUserId = InputText.Clean(ResponsibleUserId);
        Notes = InputText.Clean(Notes);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate is { } start && TargetDate is { } target && target < start)
        {
            yield return new ValidationResult("Target date can't be before the start date.", [nameof(TargetDate)]);
        }
    }

    public static ProjectInput From(ProjectDetail p) => new()
    {
        Name = p.Name,
        Description = p.Description,
        CustomerId = p.CustomerId,
        StatusId = p.StatusId,
        Priority = p.Priority,
        ResponsibleUserId = p.ResponsibleUserId,
        StartDate = p.StartDate,
        TargetDate = p.TargetDate,
        EstimatedValue = p.EstimatedValue,
        ActualValue = p.ActualValue,
        Notes = p.Notes,
    };
}

/// <summary>Money values have at most N decimal places (stored as decimal(18,2), D-10).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxDecimalsAttribute(int decimals) : ValidationAttribute($"Use at most {decimals} decimal places.")
{
    public override bool IsValid(object? value) =>
        value is not decimal d || decimal.Round(d, decimals) == d;
}
