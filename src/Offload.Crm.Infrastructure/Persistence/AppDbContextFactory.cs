using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;

namespace Offload.Crm.Infrastructure.Persistence;

/// <summary>Adapts EF's <see cref="IDbContextFactory{TContext}"/> to the application abstraction.</summary>
internal sealed class AppDbContextFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
{
    public async Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default) =>
        await factory.CreateDbContextAsync(cancellationToken);
}
