using Microsoft.EntityFrameworkCore;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.ProjectStatuses;
using Offload.Crm.Tests.Infrastructure;

namespace Offload.Crm.Tests.Services;

[Collection(DatabaseCollection.Name)]
public sealed class ProjectStatusServiceTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly ProjectStatusService _service = new(db);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_is_in_sort_order_with_exactly_one_default()
    {
        var list = await _service.ListAsync();

        Assert.Equal(["Lead", "Analysis", "Proposal", "Development", "Testing", "Delivered", "Maintenance", "Cancelled"], list.Select(s => s.Name));
        Assert.Equal("Lead", Assert.Single(list, s => s.IsDefault).Name);
    }

    [Fact]
    public async Task Create_appends_and_validates()
    {
        var created = await _service.CreateAsync(new ProjectStatusInput { Name = "  On hold ", Color = "#abcdef", IsActive = false });

        Assert.Equal("On hold", created.Name);
        Assert.Equal("#ABCDEF", created.Color);
        Assert.Equal(9, created.SortOrder);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.CreateAsync(new ProjectStatusInput { Name = "on HOLD", Color = "green", IsActive = true, IsClosed = true }));
        Assert.Equal([ProjectStatusService.DuplicateNameMessage], ex.Errors[nameof(ProjectStatusInput.Name)]);
        Assert.Contains(nameof(ProjectStatusInput.Color), ex.Errors.Keys);
        Assert.Equal([ProjectStatusService.BothFlagsMessage], ex.Errors[nameof(ProjectStatusInput.IsClosed)]);
    }

    [Fact]
    public async Task Delete_is_blocked_when_in_use_or_default()
    {
        var customer = await db.AddCustomerAsync("C");
        await db.AddProjectAsync(customer.Id); // uses the default status (Lead)
        var analysis = await db.StatusIdAsync("Analysis");
        await using (var ctx = db.CreateContext())
        {
            var p = await ctx.Projects.FirstAsync();
            p.StatusId = analysis;
            await ctx.SaveChangesAsync();
        }

        var inUse = await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(analysis));
        Assert.Contains("1 project uses it", inUse.Message);

        var lead = await db.StatusIdAsync("Lead");
        var isDefault = await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(lead));
        Assert.Contains("default", isDefault.Message);

        await _service.DeleteAsync(await db.StatusIdAsync("Cancelled"));
        Assert.Equal(7, (await _service.ListAsync()).Count);
    }

    [Fact]
    public async Task Set_default_moves_the_flag()
    {
        var testing = await db.StatusIdAsync("Testing");

        await _service.SetDefaultAsync(testing);

        var list = await _service.ListAsync();
        Assert.Equal("Testing", Assert.Single(list, s => s.IsDefault).Name);
    }

    [Fact]
    public async Task Create_as_default_and_update_to_default_keep_a_single_default()
    {
        var created = await _service.CreateAsync(new ProjectStatusInput { Name = "New default", Color = "#000000", IsDefault = true });
        Assert.True(created.IsDefault);

        var analysis = await db.StatusIdAsync("Analysis");
        await _service.UpdateAsync(analysis, new ProjectStatusInput { Name = "Analysis", Color = "#0DCAF0", IsActive = true, IsDefault = true });

        var list = await _service.ListAsync();
        Assert.Equal("Analysis", Assert.Single(list, s => s.IsDefault).Name);
    }

    [Fact]
    public async Task Default_cant_be_unset_directly()
    {
        var lead = await db.StatusIdAsync("Lead");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            _service.UpdateAsync(lead, new ProjectStatusInput { Name = "Lead", Color = "#6C757D", IsActive = true, IsDefault = false }));

        Assert.Contains(nameof(ProjectStatusInput.IsDefault), ex.Errors.Keys);
    }

    [Fact]
    public async Task Move_swaps_neighbours_and_ignores_edges()
    {
        var analysis = await db.StatusIdAsync("Analysis");

        await _service.MoveAsync(analysis, up: true);
        await _service.MoveAsync(analysis, up: true); // already first

        var names = (await _service.ListAsync()).Select(s => s.Name).ToList();
        Assert.Equal("Analysis", names[0]);
        Assert.Equal("Lead", names[1]);
        Assert.Equal(Enumerable.Range(1, 8), (await _service.ListAsync()).Select(s => s.SortOrder));
    }

    [Fact]
    public async Task Rename_is_visible_on_projects()
    {
        var customer = await db.AddCustomerAsync("C");
        await db.AddProjectAsync(customer.Id);
        var lead = await db.StatusIdAsync("Lead");

        await _service.UpdateAsync(lead, new ProjectStatusInput { Name = "Prospect", Color = "#6C757D", IsActive = true, IsDefault = true });

        await using var ctx = db.CreateContext();
        Assert.Equal("Prospect", await ctx.Projects.Select(p => p.Status.Name).SingleAsync());
    }
}
