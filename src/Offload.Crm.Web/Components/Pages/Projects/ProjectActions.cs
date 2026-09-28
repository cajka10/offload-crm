using MudBlazor;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Projects;
using Offload.Crm.Web.Localization;
using Offload.Crm.Web.Ui;

namespace Offload.Crm.Web.Components.Pages.Projects;

/// <summary>Project actions shared by the list and the detail page.</summary>
internal static class ProjectActions
{
    /// <summary>Confirms (D-07: tasks deleted, activities kept) and deletes. Returns true when deleted.</summary>
    public static async Task<bool> DeleteAsync(
        int id, string name, IProjectService service, IDialogService dialogs, ISnackbar snackbar, ILogger logger)
    {
        ProjectDetail project;
        try
        {
            project = await service.GetAsync(id);
        }
        catch (NotFoundException)
        {
            snackbar.Add("This project was already deleted.", Severity.Info);
            return true;
        }

        // D-29: counted phrases need the right plural, so this message is translated here, not by the dialog helper.
        var tasks = Text.Plural(project.TaskCount, "{0} task", "{0} tasks", "{0} úloha", "{0} úlohy", "{0} úloh");
        var activities = Text.Plural(project.ActivityCount, "{0} activity", "{0} activities", "{0} aktivita", "{0} aktivity", "{0} aktivít");
        var confirmed = await dialogs.ConfirmDeleteAsync(
            "Delete project",
            Text.T("Delete \"{0}\"? Its {1} will be deleted too. Its {2} will be kept and stay linked to the customer. This cannot be undone.", name, tasks, activities));
        if (!confirmed)
        {
            return false;
        }

        try
        {
            await service.DeleteAsync(id);
            snackbar.Add($"Project \"{name}\" deleted.", Severity.Success);
            return true;
        }
        catch (NotFoundException)
        {
            snackbar.Add("This project was already deleted.", Severity.Info);
            return true;
        }
        catch (SessionExpiredException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deleting project {Id} failed", id);
            snackbar.Add("Deleting failed. Please try again.", Severity.Error);
            return false;
        }
    }
}
