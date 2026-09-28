using Microsoft.EntityFrameworkCore;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Tests.Infrastructure;

/// <summary>Helpers that insert entities directly (bypassing services) to arrange test scenarios.</summary>
public static class TestData
{
    public static async Task<ApplicationUser> AddUserAsync(this DatabaseFixture db, string email = "author@test.local")
    {
        await using var ctx = db.CreateContext();
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Test Author",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    public static async Task<Customer> AddCustomerAsync(
        this DatabaseFixture db, string name, string? regNo = null, CustomerStatus status = CustomerStatus.Lead)
    {
        await using var ctx = db.CreateContext();
        var customer = new Customer { CompanyName = name, RegistrationNo = regNo, Status = status };
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();
        return customer;
    }

    public static async Task<Project> AddProjectAsync(this DatabaseFixture db, int customerId, string name = "Project")
    {
        await using var ctx = db.CreateContext();
        var status = await ctx.ProjectStatuses.SingleAsync(s => s.IsDefault);
        var project = new Project { Name = name, CustomerId = customerId, StatusId = status.Id };
        ctx.Projects.Add(project);
        await ctx.SaveChangesAsync();
        return project;
    }

    public static async Task<Activity> AddActivityAsync(
        this DatabaseFixture db, string authorId, DateTime dateUtc, int? customerId = null, int? projectId = null)
    {
        await using var ctx = db.CreateContext();
        var activity = new Activity
        {
            Type = ActivityType.Note,
            Date = dateUtc,
            Description = "Test activity",
            AuthorId = authorId,
            CustomerId = customerId,
            ProjectId = projectId,
        };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();
        return activity;
    }

    public static async Task<Contact> AddContactAsync(this DatabaseFixture db, int customerId, string first, string last)
    {
        await using var ctx = db.CreateContext();
        var contact = new Contact { CustomerId = customerId, FirstName = first, LastName = last };
        ctx.Contacts.Add(contact);
        await ctx.SaveChangesAsync();
        return contact;
    }
}

public static class Phase2TestData
{
    public static async Task<TaskItem> AddTaskAsync(
        this DatabaseFixture db, int projectId, string title = "Task", TaskItemStatus status = TaskItemStatus.Todo,
        string? assigneeId = null, DateOnly? due = null)
    {
        await using var ctx = db.CreateContext();
        var task = new TaskItem { ProjectId = projectId, Title = title, AssignedUserId = assigneeId, DueDate = due };
        task.ChangeStatus(status, DateTime.UtcNow);
        ctx.Tasks.Add(task);
        await ctx.SaveChangesAsync();
        return task;
    }

    public static async Task<int> StatusIdAsync(this DatabaseFixture db, string name)
    {
        await using var ctx = db.CreateContext();
        return await ctx.ProjectStatuses.Where(s => s.Name == name).Select(s => s.Id).SingleAsync();
    }

    public static async Task SetUserActiveAsync(this DatabaseFixture db, string userId, bool active)
    {
        await using var ctx = db.CreateContext();
        var user = await ctx.Users.SingleAsync(u => u.Id == userId);
        user.IsActive = active;
        await ctx.SaveChangesAsync();
    }
}
