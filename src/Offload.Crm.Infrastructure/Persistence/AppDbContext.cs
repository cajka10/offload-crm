using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IAppDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectStatus> ProjectStatuses => Set<ProjectStatus>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Activity> Activities => Set<Activity>();

    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable strings (T-03).
        configurationBuilder.Properties<CustomerStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Priority>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TaskItemStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ActivityType>().HaveConversion<string>().HaveMaxLength(20);
    }
}
