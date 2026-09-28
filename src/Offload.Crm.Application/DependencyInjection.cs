using Microsoft.Extensions.DependencyInjection;
using Offload.Crm.Application.Activities;
using Offload.Crm.Application.Contacts;
using Offload.Crm.Application.Customers;
using Offload.Crm.Application.Dashboard;
using Offload.Crm.Application.Projects;
using Offload.Crm.Application.ProjectStatuses;
using Offload.Crm.Application.Tasks;
using Offload.Crm.Application.Users;

namespace Offload.Crm.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IContactService, ContactService>();
        services.AddScoped<IProjectStatusService, ProjectStatusService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IDashboardService, DashboardService>();
        return services;
    }
}
