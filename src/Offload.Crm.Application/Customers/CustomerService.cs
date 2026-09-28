
using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Customers;

public sealed class CustomerService(IAppDbContextFactory dbFactory) : ICustomerService
{
    public const string DuplicateRegistrationNoMessage = "A customer with this registration no. (IČO) already exists.";

    private static readonly SortMap<CustomerRow> Sorts = new SortMap<CustomerRow>(CustomerSort.CompanyName, r => r.Customer.Id)
        .Add(CustomerSort.CompanyName, r => r.Customer.CompanyName)
        .Add(CustomerSort.RegistrationNo, r => r.Customer.RegistrationNo)
        .Add(CustomerSort.City, r => r.Customer.City)
        .AddEnum(CustomerSort.Status, r => r.Customer.Status)
        .Add(CustomerSort.Email, r => r.Customer.Email)
        .Add(CustomerSort.Phone, r => r.Customer.Phone)
        .Add(CustomerSort.ProjectCount, r => r.ProjectCount)
        .Add(CustomerSort.LastActivity, r => r.LastActivityAt)
        .Add(CustomerSort.Created, r => r.Customer.CreatedAt);

    public async Task<PagedResult<CustomerListItem>> ListAsync(CustomerListQuery query, CancellationToken ct = default)
    {
        Guard.EnumDefined(query.Status, "status");

        await using var db = await dbFactory.CreateAsync(ct);
        IQueryable<Customer> customers = db.Customers.AsNoTracking();

        if (query.Status is { } status)
        {
            customers = status == CustomerStatus.Active
                ? customers.Where(Predicates.ActiveCustomer) // the dashboard KPI uses the same predicate (D-23)
                : customers.Where(c => c.Status == status);
        }

        // D-16: case-insensitive "contains" over name, IČO, VAT, e-mail and contact names.
        if (query.NormalizedSearch is { } term)
        {
            customers = customers.Where(c =>
                c.CompanyName.ToLower().Contains(term)
                || (c.RegistrationNo != null && c.RegistrationNo.ToLower().Contains(term))
                || (c.VatId != null && c.VatId.ToLower().Contains(term))
                || (c.Email != null && c.Email.ToLower().Contains(term))
                || c.Contacts.Any(p =>
                    ((p.FirstName ?? string.Empty) + " " + p.LastName).ToLower().Contains(term)
                    || (p.LastName + " " + (p.FirstName ?? string.Empty)).ToLower().Contains(term)));
        }

        var activities = db.Activities;
        var rows = customers.Select(c => new CustomerRow
        {
            Customer = c,
            ProjectCount = c.Projects.Count,
            // D-06: latest activity linked directly or via one of the customer's projects.
            LastActivityAt = activities
                .Where(a => a.CustomerId == c.Id || (a.ProjectId != null && a.Project!.CustomerId == c.Id))
                .Max(a => (DateTime?)a.Date),
        });

        return await Sorts.Apply(rows, query.Sort, query.Descending).ToPagedResultAsync(
            query,
            q => q.Select(r => new CustomerListItem(
                r.Customer.Id,
                r.Customer.CompanyName,
                r.Customer.RegistrationNo,
                r.Customer.City,
                r.Customer.Status,
                r.Customer.Email,
                r.Customer.Phone,
                r.ProjectCount,
                r.LastActivityAt,
                r.Customer.CreatedAt)),
            ct);
    }

    public async Task<IReadOnlyList<LookupItem>> LookupAsync(string? search, int take = 20, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
        return await db.Customers.AsNoTracking()
            .Where(c => term == null || c.CompanyName.ToLower().Contains(term) || (c.RegistrationNo != null && c.RegistrationNo.ToLower().Contains(term)))
            .OrderBy(c => c.CompanyName).ThenBy(c => c.Id)
            .Take(Math.Clamp(take, 1, 100))
            .Select(c => new LookupItem(c.Id, c.CompanyName))
            .ToListAsync(ct);
    }

    public async Task<CustomerDetail> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        return await GetDetailAsync(db, id, ct);
    }

    public async Task<CustomerDetail> CreateAsync(CustomerInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        await ValidateAsync(db, input, null, ct);

        var customer = new Customer();
        Apply(customer, input);
        db.Customers.Add(customer);
        await SaveAsync(db, input, null, ct);

        return await GetDetailAsync(db, customer.Id, ct);
    }

    public async Task<CustomerDetail> UpdateAsync(int id, CustomerInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Customer", id);

        await ValidateAsync(db, input, id, ct);

        Apply(customer, input);
        await SaveAsync(db, input, id, ct);

        return await GetDetailAsync(db, id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Customer", id);

        // D-07: blocked while the customer has projects.
        var projectCount = await db.Projects.CountAsync(p => p.CustomerId == id, ct);
        if (projectCount > 0)
        {
            var noun = projectCount == 1 ? "project" : "projects";
            throw new ConflictException($"Customer has {projectCount} {noun}. Delete or reassign them first.");
        }

        // Contacts and customer-only activities are removed by the database cascade.
        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<CustomerDetail> GetDetailAsync(IAppDbContext db, int id, CancellationToken ct)
    {
        var activities = db.Activities;
        var projects = db.Projects;
        var tasks = db.Tasks;
        var activeProject = Predicates.ActiveProject;
        var openTask = Predicates.OpenTask;
        var detail = await db.Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerDetail(
                c.Id,
                c.CompanyName,
                c.RegistrationNo,
                c.VatId,
                c.Status,
                c.Email,
                c.Phone,
                c.Website,
                c.Street,
                c.City,
                c.PostalCode,
                c.Country,
                c.Notes,
                c.Projects.Count,
                c.Contacts.Count,
                activities
                    .Where(a => a.CustomerId == c.Id || (a.ProjectId != null && a.Project!.CustomerId == c.Id))
                    .Max(a => (DateTime?)a.Date),
                c.CreatedAt,
                c.UpdatedAt,
                // T-26 summary: same predicates as the dashboard (D-23)
                projects.Where(p => p.CustomerId == c.Id).Where(activeProject).Count(),
                tasks.Where(t => t.Project.CustomerId == c.Id).Where(openTask).Count(),
                projects.Where(p => p.CustomerId == c.Id).Where(activeProject).Sum(p => p.EstimatedValue ?? 0)))
            .FirstOrDefaultAsync(ct);

        return detail ?? throw new NotFoundException("Customer", id);
    }

    private static async Task ValidateAsync(IAppDbContext db, CustomerInput input, int? currentId, CancellationToken ct)
    {
        input.Normalize();

        var errors = ModelValidator.Validate(input);

        if (!errors.ContainsKey(nameof(CustomerInput.RegistrationNo))
            && await IsRegistrationNoTakenAsync(db, input.RegistrationNo, currentId, ct))
        {
            errors[nameof(CustomerInput.RegistrationNo)] = [DuplicateRegistrationNoMessage];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }

    private static async Task<bool> IsRegistrationNoTakenAsync(
        IAppDbContext db, string? registrationNo, int? currentId, CancellationToken ct)
    {
        var normalized = Customer.NormalizeRegistrationNo(registrationNo);
        if (normalized is null)
        {
            return false;
        }

        return await db.Customers.AnyAsync(
            c => c.RegistrationNoNormalized == normalized && (currentId == null || c.Id != currentId),
            ct);
    }

    /// <summary>Saves; if a concurrent request grabbed the same IČO the unique index fails and we report a field error.</summary>
    private static async Task SaveAsync(IAppDbContext db, CustomerInput input, int? currentId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (await IsRegistrationNoTakenAsync(db, input.RegistrationNo, currentId, ct))
            {
                throw new AppValidationException(nameof(CustomerInput.RegistrationNo), DuplicateRegistrationNoMessage);
            }

            throw;
        }
    }

    private static void Apply(Customer customer, CustomerInput input)
    {
        customer.CompanyName = input.CompanyName!;
        customer.RegistrationNo = input.RegistrationNo;
        customer.VatId = input.VatId;
        customer.Status = input.Status;
        customer.Email = input.Email;
        customer.Phone = input.Phone;
        customer.Website = input.Website;
        customer.Street = input.Street;
        customer.City = input.City;
        customer.PostalCode = input.PostalCode;
        customer.Country = input.Country;
        customer.Notes = input.Notes;
    }

    /// <summary>Intermediate projection so sorting by computed columns translates to SQL.</summary>
    private sealed class CustomerRow
    {
        public Customer Customer { get; init; } = null!;

        public int ProjectCount { get; init; }

        public DateTime? LastActivityAt { get; init; }
    }
}
