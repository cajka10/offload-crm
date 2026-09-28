using MudBlazor;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Web.Ui;

/// <summary>Display texts (translated, D-29) and colors for enums, kept in one place.</summary>
public static class Labels
{
    public static string Of(ActivityType type) => Localization.Text.T(DisplayNames.Of(type));

    public static string Of(TaskItemStatus status) => Localization.Text.T(DisplayNames.Of(status));

    public static string Of(Priority priority) => Localization.Text.T(priority.ToString());

    public static string Of(CustomerStatus status) => Localization.Text.T(status.ToString());

    public static string ActivityIcon(ActivityType type) => type switch
    {
        ActivityType.Call => Icons.Material.Filled.Phone,
        ActivityType.Email => Icons.Material.Filled.Email,
        ActivityType.Meeting => Icons.Material.Filled.Groups,
        ActivityType.Note => Icons.Material.Filled.StickyNote2,
        _ => Icons.Material.Filled.Event,
    };

    /// <summary>D-05: Critical red, High orange (#E8912D), Medium blue-grey, Low grey.</summary>
    public static string PriorityStyle(Priority priority) => priority switch
    {
        Priority.Critical => "background-color: #C62828; color: #fff;",
        Priority.High => "background-color: #E8912D; color: #fff;",
        Priority.Medium => "background-color: #607D8B; color: #fff;",
        _ => "background-color: #E0E0E0; color: #424242;",
    };

    public static Color TaskStatusColor(TaskItemStatus status) => status switch
    {
        TaskItemStatus.InProgress => Color.Info,
        TaskItemStatus.Blocked => Color.Error,
        TaskItemStatus.Done => Color.Success,
        _ => Color.Default,
    };

    /// <summary>Readable text color on a status background color.</summary>
    public static string TextOn(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            return "#fff";
        }

        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return (r * 299 + g * 587 + b * 114) / 1000 > 150 ? "#212121" : "#fff";
    }
}
