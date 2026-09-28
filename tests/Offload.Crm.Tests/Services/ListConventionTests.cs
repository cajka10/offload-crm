using Offload.Crm.Application.Common;
using Offload.Crm.Application.Customers;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

/// <summary>T-38 / D-20: paging, nulls-last sorting, enum order and enum filter validation on a real query.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ListConventionTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly CustomerService _service = new(db);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nulls_sort_last_in_both_directions(bool descending)
    {
        var author = await db.AddUserAsync();
        var none = await db.AddCustomerAsync("No activity");
        var old = await db.AddCustomerAsync("Old activity");
        var recent = await db.AddCustomerAsync("Recent activity");
        await db.AddActivityAsync(author.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), customerId: old.Id);
        await db.AddActivityAsync(author.Id, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), customerId: recent.Id);

        var result = await _service.ListAsync(new CustomerListQuery { Sort = CustomerSort.LastActivity, Descending = descending });

        var expected = descending ? new[] { recent.Id, old.Id, none.Id } : [old.Id, recent.Id, none.Id];
        Assert.Equal(expected, result.Items.Select(c => c.Id));
    }

    [Theory]
    [InlineData(CustomerSort.RegistrationNo)]
    [InlineData(CustomerSort.City)]
    [InlineData(CustomerSort.Email)]
    [InlineData(CustomerSort.Phone)]
    public async Task Empty_text_columns_sort_last_descending(string sort)
    {
        await db.AddCustomerAsync("Empty");
        await _service.CreateAsync(new CustomerInput
        {
            CompanyName = "Filled",
            RegistrationNo = "123",
            City = "Brno",
            Email = "a@b.cz",
            Phone = "123",
        });

        var desc = await _service.ListAsync(new CustomerListQuery { Sort = sort, Descending = true });
        var asc = await _service.ListAsync(new CustomerListQuery { Sort = sort });

        Assert.Equal("Filled", desc.Items[0].CompanyName);
        Assert.Equal("Filled", asc.Items[0].CompanyName);
    }

    [Fact]
    public async Task Status_sorts_in_lifecycle_order()
    {
        await db.AddCustomerAsync("I", status: CustomerStatus.Inactive);
        await db.AddCustomerAsync("L", status: CustomerStatus.Lead);
        await db.AddCustomerAsync("A", status: CustomerStatus.Active);

        var asc = await _service.ListAsync(new CustomerListQuery { Sort = CustomerSort.Status });
        var desc = await _service.ListAsync(new CustomerListQuery { Sort = CustomerSort.Status, Descending = true });

        Assert.Equal(["L", "A", "I"], asc.Items.Select(c => c.CompanyName));
        Assert.Equal(["I", "A", "L"], desc.Items.Select(c => c.CompanyName));
    }

    [Theory]
    [InlineData(100_000_000)]
    [InlineData(int.MaxValue)]
    [InlineData(2)]
    public async Task Page_beyond_the_end_returns_an_empty_page(int page)
    {
        await db.AddCustomerAsync("Only one");

        var result = await _service.ListAsync(new CustomerListQuery { Page = page });

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(page, result.Page);
    }

    [Fact]
    public async Task Undefined_status_filter_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.ListAsync(new CustomerListQuery { Status = (CustomerStatus)99 }));

        Assert.Contains("status", ex.Errors.Keys);
    }

    [Fact]
    public async Task Undefined_status_in_input_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new CustomerInput { CompanyName = "Bad status", Status = (CustomerStatus)99 }));

        Assert.Contains(nameof(CustomerInput.Status), ex.Errors.Keys);
    }

    [Fact]
    public async Task Last_page_is_exact_and_page_size_respected()
    {
        for (var i = 0; i < 26; i++)
        {
            await db.AddCustomerAsync($"C{i:00}");
        }

        var last = await _service.ListAsync(new CustomerListQuery { Page = 2 });
        var fifty = await _service.ListAsync(new CustomerListQuery { PageSize = 50 });

        Assert.Single(last.Items);
        Assert.Equal("C25", last.Items[0].CompanyName);
        Assert.Equal(26, fifty.Items.Count);
        Assert.Equal(PagedQuery.DefaultPageSize, last.PageSize);
    }
}
