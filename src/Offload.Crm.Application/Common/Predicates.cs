using System.Linq.Expressions;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Common;

/// <summary>
/// The single definitions of D-03's "active"/"open"/"overdue" rules. Lists and the dashboard (D-23) both use these,
/// so a KPI can never disagree with the list it links to.
/// </summary>
public static class Predicates
{
    public static readonly Expression<Func<Customer, bool>> ActiveCustomer = c => c.Status == CustomerStatus.Active;

    public static readonly Expression<Func<Project, bool>> ActiveProject = p => p.Status.IsActive;

    public static readonly Expression<Func<TaskItem, bool>> OpenTask = t => t.Status != TaskItemStatus.Done;

    public static Expression<Func<TaskItem, bool>> OverdueTask(DateOnly today) =>
        t => t.DueDate < today && t.Status != TaskItemStatus.Done;
}
