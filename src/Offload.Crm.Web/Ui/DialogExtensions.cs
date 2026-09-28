using MudBlazor;

namespace Offload.Crm.Web.Ui;

public static class DialogExtensions
{
    /// <summary>Confirmation for destructive actions (D-15). Returns true only when the user confirms.</summary>
    public static async Task<bool> ConfirmDeleteAsync(this IDialogService dialogs, string title, string message, string confirmText = "Delete")
    {
        // D-29: callers pass English (also with values, e.g. names); translated here in one place.
        var result = await dialogs.ShowMessageBox(
            Localization.Text.T(title),
            Localization.Text.T(message),
            yesText: Localization.Text.T(confirmText),
            cancelText: Localization.Text.T("Cancel"),
            options: new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true, CloseOnEscapeKey = true });
        return result == true;
    }

    /// <summary>Standard options for create/edit dialogs.</summary>
    public static DialogOptions FormOptions(MaxWidth maxWidth = MaxWidth.Medium) => new()
    {
        MaxWidth = maxWidth,
        FullWidth = true,
        CloseOnEscapeKey = true,
        BackdropClick = false,
        CloseButton = true,
    };
}
