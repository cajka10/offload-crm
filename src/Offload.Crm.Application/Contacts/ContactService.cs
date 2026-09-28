using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Entities;

namespace Offload.Crm.Application.Contacts;

/// <summary>Contact use cases. Business errors are reported with <see cref="AppException"/> subclasses.</summary>
public interface IContactService
{
    /// <summary>All contacts (optionally of one customer), searchable by name/e-mail/position, sorted and paged.</summary>
    Task<PagedResult<ContactDto>> ListAsync(ContactListQuery query, CancellationToken ct = default);

    /// <summary>Contacts of a customer ordered by name; <see cref="NotFoundException"/> when the customer doesn't exist.</summary>
    Task<IReadOnlyList<ContactDto>> ListByCustomerAsync(int customerId, CancellationToken ct = default);

    /// <summary>Gets one contact or throws <see cref="NotFoundException"/>.</summary>
    Task<ContactDto> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Creates a contact for an existing customer.</summary>
    Task<ContactDto> CreateAsync(int customerId, ContactInput input, CancellationToken ct = default);

    /// <summary>Updates a contact.</summary>
    Task<ContactDto> UpdateAsync(int id, ContactInput input, CancellationToken ct = default);

    /// <summary>Hard-deletes a contact (D-07).</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class ContactService(IAppDbContextFactory dbFactory) : IContactService
{
    private static readonly SortMap<Contact> Sorts = new SortMap<Contact>("name", c => c.Id)
        .Add("name", c => c.LastName + " " + (c.FirstName ?? string.Empty))
        .Add("customer", c => c.Customer.CompanyName)
        .Add("email", c => c.Email)
        .Add("created", c => c.CreatedAt);

    public async Task<PagedResult<ContactDto>> ListAsync(ContactListQuery query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        IQueryable<Contact> contacts = db.Contacts.AsNoTracking();
        if (query.CustomerId is { } customerId)
        {
            contacts = contacts.Where(c => c.CustomerId == customerId);
        }

        if (query.NormalizedSearch is { } term)
        {
            contacts = contacts.Where(c =>
                ((c.FirstName ?? string.Empty) + " " + c.LastName).ToLower().Contains(term)
                || (c.Email != null && c.Email.ToLower().Contains(term))
                || (c.Position != null && c.Position.ToLower().Contains(term)));
        }

        var page = await Sorts.Apply(contacts, query.Sort, query.Descending).ToPagedResultAsync(query, ct);
        return new PagedResult<ContactDto>(page.Items.Select(ToDto).ToList(), page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<IReadOnlyList<ContactDto>> ListByCustomerAsync(int customerId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct))
        {
            throw new NotFoundException("Customer", customerId);
        }

        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Id)
            .ToListAsync(ct);

        return contacts.Select(ToDto).ToList();
    }

    public async Task<ContactDto> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Contact", id);
        return ToDto(contact);
    }

    public async Task<ContactDto> CreateAsync(int customerId, ContactInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct))
        {
            throw new NotFoundException("Customer", customerId);
        }

        input.Normalize();
        ModelValidator.ThrowIfInvalid(input);

        var contact = new Contact { CustomerId = customerId };
        Apply(contact, input);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);
        return ToDto(contact);
    }

    public async Task<ContactDto> UpdateAsync(int id, ContactInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Contact", id);

        input.Normalize();
        ModelValidator.ThrowIfInvalid(input);

        Apply(contact, input);
        await db.SaveChangesAsync(ct);
        return ToDto(contact);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Contact", id);
        db.Contacts.Remove(contact);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Contact contact, ContactInput input)
    {
        contact.FirstName = input.FirstName;
        contact.LastName = input.LastName!;
        contact.Position = input.Position;
        contact.Email = input.Email;
        contact.Phone = input.Phone;
        contact.Notes = input.Notes;
    }

    private static ContactDto ToDto(Contact c) => new(
        c.Id,
        c.CustomerId,
        c.FirstName,
        c.LastName,
        string.IsNullOrWhiteSpace(c.FirstName) ? c.LastName : $"{c.FirstName} {c.LastName}",
        c.Position,
        c.Email,
        c.Phone,
        c.Notes,
        c.CreatedAt,
        c.UpdatedAt);
}
