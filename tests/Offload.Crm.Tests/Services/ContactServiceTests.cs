using Offload.Crm.Application.Common;
using Offload.Crm.Application.Contacts;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

[Collection(DatabaseCollection.Name)]
public sealed class ContactServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly ContactService _service = new(db);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Crud_round_trip()
    {
        var customer = await db.AddCustomerAsync("Firma");

        var created = await _service.CreateAsync(customer.Id, new ContactInput { FirstName = " Jiřina ", LastName = "Šťastná", Email = "j@firma.cz" });
        Assert.Equal("Jiřina Šťastná", created.FullName);

        var updated = await _service.UpdateAsync(created.Id, new ContactInput { LastName = "Nová", Position = "CEO" });
        Assert.Equal("Nová", updated.FullName);
        Assert.Null(updated.FirstName);

        var list = await _service.ListByCustomerAsync(customer.Id);
        Assert.Single(list);

        await _service.DeleteAsync(created.Id);
        Assert.Empty(await _service.ListByCustomerAsync(customer.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(created.Id));
    }

    [Fact]
    public async Task Create_validates_input()
    {
        var customer = await db.AddCustomerAsync("Firma");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(customer.Id, new ContactInput { FirstName = "Jan", Email = "x", Phone = "abc" }));

        Assert.Contains(nameof(ContactInput.LastName), ex.Errors.Keys);
        Assert.Contains(nameof(ContactInput.Email), ex.Errors.Keys);
        Assert.Contains(nameof(ContactInput.Phone), ex.Errors.Keys);
    }

    [Fact]
    public async Task Unknown_customer_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CreateAsync(999, new ContactInput { LastName = "X" }));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ListByCustomerAsync(999));
    }
}
