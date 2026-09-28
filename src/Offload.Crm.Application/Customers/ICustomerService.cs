using Offload.Crm.Application.Common;

namespace Offload.Crm.Application.Customers;

/// <summary>Customer use cases. Business errors are reported with <see cref="AppException"/> subclasses.</summary>
public interface ICustomerService
{
    /// <summary>Search (D-16), status filter, sort (<see cref="CustomerSort"/>) and paging.</summary>
    Task<PagedResult<CustomerListItem>> ListAsync(CustomerListQuery query, CancellationToken ct = default);

    /// <summary>Customers for autocompletes (name or IČO contains <paramref name="search"/>), ordered by name.</summary>
    Task<IReadOnlyList<LookupItem>> LookupAsync(string? search, int take = 20, CancellationToken ct = default);

    /// <summary>Gets one customer or throws <see cref="NotFoundException"/>.</summary>
    Task<CustomerDetail> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Validates (D-12, unique IČO) and creates; throws <see cref="AppValidationException"/>.</summary>
    Task<CustomerDetail> CreateAsync(CustomerInput input, CancellationToken ct = default);

    /// <summary>Validates and updates; throws <see cref="AppValidationException"/> or <see cref="NotFoundException"/>.</summary>
    Task<CustomerDetail> UpdateAsync(int id, CustomerInput input, CancellationToken ct = default);

    /// <summary>Deletes with contacts/activities; blocked with <see cref="ConflictException"/> while it has projects (D-07).</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
