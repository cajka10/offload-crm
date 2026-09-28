using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Common;

/// <summary>User-facing names of enum values, shared by the UI and API texts (D-11, D-19).</summary>
public static class DisplayNames
{
    public static string Of(ActivityType type) => type switch
    {
        ActivityType.Call => "Phone call", // D-19
        ActivityType.Email => "E-mail",
        _ => type.ToString(),
    };

    public static string Of(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Todo => "To do",
        TaskItemStatus.InProgress => "In progress",
        _ => status.ToString(),
    };
}
