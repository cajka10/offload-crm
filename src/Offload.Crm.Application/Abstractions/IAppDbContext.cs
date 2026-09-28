using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Offload.Crm.Domain.Entities;

namespace Offload.Crm.Application.Abstractions;

/// <summary>
/// Persistence abstraction used by the services. Implemented by the EF Core <c>AppDbContext</c> in Infrastructure.
/// UI and API code never talk to it directly; they go through services (D-01).
/// </summary>
public interface IAppDbContext : IAsyncDisposable
{
    DbSet<Customer> Customers { get; }

    DbSet<Contact> Contacts { get; }

    DbSet<Project> Projects { get; }

    DbSet<ProjectStatus> ProjectStatuses { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<Activity> Activities { get; }

    DbSet<ApplicationUser> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates a short-lived context per service operation. Blazor Server circuits are long-lived, so a
/// circuit-scoped DbContext would be shared across concurrent UI events; one context per operation avoids that.
/// </summary>
public interface IAppDbContextFactory
{
    Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>The signed-in user of the current request or Blazor circuit.</summary>
public interface ICurrentUser
{
    /// <summary>The user id, or null when nobody is signed in (e.g. background work).</summary>
    Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default);
}
