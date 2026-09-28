using MudBlazor;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Tasks;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Web.Ui;

namespace Offload.Crm.Web.Components.Pages.Tasks;

/// <summary>Task actions shared by the Tasks page (list + Kanban) and the project detail page.</summary>
internal static class TaskActions
{
    public static async Task<bool> ChangeStatusAsync(ITaskService service, ISnackbar snackbar, ILogger logger, TaskDto task, TaskItemStatus status)
    {
        if (task.Status == status)
        {
            return true;
        }

        try
        {
            await service.ChangeStatusAsync(task.Id, status);
            snackbar.Add($"\"{task.Title}\" moved to {Labels.Of(status)}.", Severity.Success);
            return true;
        }
        catch (NotFoundException)
        {
            snackbar.Add("This task no longer exists.", Severity.Warning);
            return false;
        }
        catch (SessionExpiredException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Changing status of task {Id} failed", task.Id);
            snackbar.Add("The status could not be changed. Please try again.", Severity.Error);
            return false;
        }
    }

    public static async Task<bool> DeleteAsync(ITaskService service, IDialogService dialogs, ISnackbar snackbar, ILogger logger, TaskDto task)
    {
        if (!await dialogs.ConfirmDeleteAsync("Delete task", $"Delete the task \"{task.Title}\"? This cannot be undone."))
        {
            return false;
        }

        try
        {
            await service.DeleteAsync(task.Id);
            snackbar.Add("Task deleted.", Severity.Success);
            return true;
        }
        catch (NotFoundException)
        {
            snackbar.Add("This task was already deleted.", Severity.Info);
            return true;
        }
        catch (SessionExpiredException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deleting task {Id} failed", task.Id);
            snackbar.Add("Deleting failed. Please try again.", Severity.Error);
            return false;
        }
    }
}
