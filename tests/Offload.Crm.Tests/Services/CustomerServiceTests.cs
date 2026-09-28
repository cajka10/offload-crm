using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Customers;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

[Collection(DatabaseCollection.Name)]
public sealed class CustomerServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly CustomerService _service = new(db);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_normalizes_and_persists_with_timestamps()
    {
        var created = await _service.CreateAsync(new CustomerInput
        {
            CompanyName = "  Žluťoučký kůň a.s.  ",
            RegistrationNo = " 452 746 49 ",
            Website = "zlutykun.cz",
            Email = "  ",
            Status = CustomerStatus.Active,
        });

        Assert.Equal("Žluťoučký kůň a.s.", created.CompanyName);
        Assert.Equal("452 746 49", created.RegistrationNo);
        Assert.Equal("https://zlutykun.cz", created.Website);
        Assert.Null(created.Email);
        Assert.Equal(CustomerStatus.Active, created.Status);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
    }

    [Fact]
    public async Task Create_with_invalid_input_throws_field_errors()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _service.CreateAsync(new CustomerInput
        {
            CompanyName = " ",
            Email = "nope",
        }));

        Assert.Contains(nameof(CustomerInput.CompanyName), ex.Errors.Keys);
        Assert.Contains(nameof(CustomerInput.Email), ex.Errors.Keys);
        await using var ctx = db.CreateContext();
        Assert.False(await ctx.Customers.AnyAsync());
    }

    [Theory]
    [InlineData("25596641")]
    [InlineData("255 966 41")]
    [InlineData(" 25596641 ")]
    public async Task Duplicate_registration_no_is_rejected_regardless_of_spaces(string duplicate)
    {
        await _service.CreateAsync(new CustomerInput { CompanyName = "First", RegistrationNo = "25596641" });

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new CustomerInput { CompanyName = "Second", RegistrationNo = duplicate }));

        Assert.Equal([CustomerService.DuplicateRegistrationNoMessage], ex.Errors[nameof(CustomerInput.RegistrationNo)]);
    }

    [Fact]
    public async Task Duplicate_registration_no_is_case_insensitive()
    {
        await _service.CreateAsync(new CustomerInput { CompanyName = "First", RegistrationNo = "cz123abc" });

        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new CustomerInput { CompanyName = "Second", RegistrationNo = "CZ123ABC" }));
    }

    [Fact]
    public async Task Update_can_keep_own_registration_no_but_not_take_another()
    {
        var first = await _service.CreateAsync(new CustomerInput { CompanyName = "First", RegistrationNo = "111" });
        var second = await _service.CreateAsync(new CustomerInput { CompanyName = "Second", RegistrationNo = "222" });

        var updated = await _service.UpdateAsync(first.Id, new CustomerInput { CompanyName = "First renamed", RegistrationNo = "111" });
        Assert.Equal("First renamed", updated.CompanyName);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.UpdateAsync(second.Id, new CustomerInput { CompanyName = "Second", RegistrationNo = "111" }));
    }

    [Fact]
    public async Task Multiple_customers_without_registration_no_are_allowed()
    {
        await _service.CreateAsync(new CustomerInput { CompanyName = "A" });
        await _service.CreateAsync(new CustomerInput { CompanyName = "B", RegistrationNo = "  " });

        var list = await _service.ListAsync(new CustomerListQuery());
        Assert.Equal(2, list.TotalCount);
    }

    [Fact]
    public async Task Update_sets_updated_at_and_keeps_created_at()
    {
        db.Clock.Now = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        try
        {
            var created = await _service.CreateAsync(new CustomerInput { CompanyName = "Clock" });
            db.Clock.Now = db.Clock.Now.Value.AddDays(1);

            var updated = await _service.UpdateAsync(created.Id, new CustomerInput { CompanyName = "Clock 2" });

            Assert.Equal(created.CreatedAt, updated.CreatedAt);
            Assert.Equal(created.CreatedAt.AddDays(1), updated.UpdatedAt);
        }
        finally
        {
            db.Clock.Now = null;
        }
    }

    [Fact]
    public async Task Get_and_update_unknown_id_throw_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(12345));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateAsync(12345, new CustomerInput { CompanyName = "X" }));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(12345));
    }

    [Fact]
    public async Task Delete_is_blocked_while_customer_has_projects()
    {
        var customer = await db.AddCustomerAsync("Has projects");
        await db.AddProjectAsync(customer.Id, "P1");
        await db.AddProjectAsync(customer.Id, "P2");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(customer.Id));

        Assert.Equal("Customer has 2 projects. Delete or reassign them first.", ex.Message);
        Assert.NotNull(await _service.GetAsync(customer.Id));
    }

    [Fact]
    public async Task Delete_without_projects_cascades_to_contacts_and_activities()
    {
        var author = await db.AddUserAsync();
        var customer = await db.AddCustomerAsync("No projects");
        await db.AddContactAsync(customer.Id, "Jan", "Novák");
        await db.AddActivityAsync(author.Id, DateTime.UtcNow, customerId: customer.Id);
        var other = await db.AddCustomerAsync("Other");
        await db.AddContactAsync(other.Id, "Eva", "Malá");

        await _service.DeleteAsync(customer.Id);

        await using var ctx = db.CreateContext();
        Assert.False(await ctx.Customers.AnyAsync(c => c.Id == customer.Id));
        Assert.False(await ctx.Contacts.AnyAsync(c => c.CustomerId == customer.Id));
        Assert.False(await ctx.Activities.AnyAsync());
        Assert.Equal(1, await ctx.Contacts.CountAsync());
    }

    [Fact]
    public async Task Last_activity_is_max_of_direct_and_project_activities()
    {
        var author = await db.AddUserAsync();
        var customer = await db.AddCustomerAsync("With activities");
        var project = await db.AddProjectAsync(customer.Id);
        var direct = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var viaProjectOnly = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        await db.AddActivityAsync(author.Id, direct, customerId: customer.Id);
        await db.AddActivityAsync(author.Id, viaProjectOnly, projectId: project.Id); // no CustomerId set
        var quiet = await db.AddCustomerAsync("Without activities");
        var otherCustomer = await db.AddCustomerAsync("Other");
        await db.AddActivityAsync(author.Id, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), customerId: otherCustomer.Id);

        var detail = await _service.GetAsync(customer.Id);
        var list = await _service.ListAsync(new CustomerListQuery());

        Assert.Equal(viaProjectOnly, detail.LastActivityAt);
        Assert.Equal(viaProjectOnly, list.Items.Single(c => c.Id == customer.Id).LastActivityAt);
        Assert.Null(list.Items.Single(c => c.Id == quiet.Id).LastActivityAt);
        Assert.Equal(1, list.Items.Single(c => c.Id == customer.Id).ProjectCount);
    }

    [Fact]
    public async Task List_sorts_by_last_activity()
    {
        var author = await db.AddUserAsync();
        var older = await db.AddCustomerAsync("Older");
        var newer = await db.AddCustomerAsync("Newer");
        await db.AddActivityAsync(author.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), customerId: older.Id);
        await db.AddActivityAsync(author.Id, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), customerId: newer.Id);

        var asc = await _service.ListAsync(new CustomerListQuery { Sort = CustomerSort.LastActivity });
        var desc = await _service.ListAsync(new CustomerListQuery { Sort = CustomerSort.LastActivity, Descending = true });

        Assert.Equal(["Older", "Newer"], asc.Items.Select(c => c.CompanyName));
        Assert.Equal(["Newer", "Older"], desc.Items.Select(c => c.CompanyName));
    }

    [Theory]
    [InlineData(CustomerSort.CompanyName)]
    [InlineData(CustomerSort.RegistrationNo)]
    [InlineData(CustomerSort.City)]
    [InlineData(CustomerSort.Status)]
    [InlineData(CustomerSort.Email)]
    [InlineData(CustomerSort.Phone)]
    [InlineData(CustomerSort.ProjectCount)]
    [InlineData(CustomerSort.LastActivity)]
    [InlineData(CustomerSort.Created)]
    [InlineData("unknown-column")]
    public async Task List_supports_every_sort_column(string sort)
    {
        await db.AddCustomerAsync("A", "1");
        await db.AddCustomerAsync("B", "2");

        var result = await _service.ListAsync(new CustomerListQuery { Sort = sort, Descending = true });

        Assert.Equal(2, result.Items.Count);
    }

    [Theory]
    [InlineData("řehoř")]
    [InlineData("ŘEHOŘ")]
    [InlineData("stavby")]
    [InlineData("25596641")]
    [InlineData("cz2559")]
    [InlineData("info@stavby")]
    [InlineData("tomáš")]
    [InlineData("tomáš řehoř")]
    [InlineData("řehoř tomáš")]
    public async Task Search_matches_name_ico_vat_email_and_contacts_case_insensitively(string term)
    {
        var match = await _service.CreateAsync(new CustomerInput
        {
            CompanyName = "Stavby Řehoř s.r.o.",
            RegistrationNo = "25596641",
            VatId = "CZ25596641",
            Email = "info@stavby.cz",
        });
        await db.AddContactAsync(match.Id, "Tomáš", "Řehoř");
        await _service.CreateAsync(new CustomerInput { CompanyName = "Unrelated a.s." });

        var result = await _service.ListAsync(new CustomerListQuery { Search = term });

        Assert.Equal([match.Id], result.Items.Select(c => c.Id));
    }

    [Fact]
    public async Task Search_and_status_filter_combine_and_paging_works()
    {
        for (var i = 1; i <= 30; i++)
        {
            await db.AddCustomerAsync($"Firma {i:00}", status: i % 3 == 0 ? CustomerStatus.Active : CustomerStatus.Lead);
        }

        var page2 = await _service.ListAsync(new CustomerListQuery { Page = 2, PageSize = 25 });
        var filtered = await _service.ListAsync(new CustomerListQuery { Search = "firma 1", Status = CustomerStatus.Active });
        var beyond = await _service.ListAsync(new CustomerListQuery { Page = 99 });
        var none = await _service.ListAsync(new CustomerListQuery { Search = "nothing-like-this" });

        Assert.Equal(30, page2.TotalCount);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal(["Firma 12", "Firma 15", "Firma 18"], filtered.Items.Select(c => c.CompanyName));
        Assert.Empty(beyond.Items);
        Assert.Equal(0, none.TotalCount);
    }

    [Fact]
    public async Task Page_size_is_clamped()
    {
        var query = new CustomerListQuery { PageSize = 10_000, Page = -5 };

        var result = await _service.ListAsync(query);

        Assert.Equal(PagedQuery.MaxPageSize, result.PageSize);
        Assert.Equal(1, result.Page);
    }
}
