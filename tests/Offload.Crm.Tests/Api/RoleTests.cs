using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Users;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure.Seeding;
using Offload.Crm.Tests.Infrastructure;
using Offload.Crm.Web.Auth;

namespace Offload.Crm.Tests.Api;

/// <summary>D-27: Administrator vs. User role, enforced server-side for the UI services and the API.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class RoleTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string Password = "Secret123";
    private readonly TestUserContext _context = new();
    private WebApplicationFactory<Program> _factory = null!;
    private string _adminId = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _factory = await TestHost.CreateAsync(db, services: s => s.AddSingleton<IUserContext>(_context));
        using var scope = _factory.Services.CreateScope();
        _adminId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(TestHost.AdminEmail))!.Id;
        _context.As(_adminId);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Create_sets_the_role_and_the_list_shows_it()
    {
        var admin = await CreateAsync("boss@test.local", isAdmin: true);
        var user = await CreateAsync("worker@test.local");

        Assert.True(admin.IsAdmin);
        Assert.False(user.IsAdmin);
        var list = await Service(s => s.ListAsync(new UserListQuery()));
        Assert.True(list.Single(u => u.Id == _adminId).IsAdmin); // the seeded admin
        Assert.True(list.Single(u => u.Id == admin.Id).IsAdmin);
        Assert.False(list.Single(u => u.Id == user.Id).IsAdmin);
    }

    [Fact]
    public async Task Users_cant_manage_users_but_can_change_their_own_password()
    {
        var user = await CreateAsync("worker@test.local");
        _context.As(user.Id);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Service(s => s.ListAsync(new UserListQuery())));
        Assert.Equal("Only administrators can manage users.", ex.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAsync("other@test.local"));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(s => s.UpdateAsync(user.Id, Update(user, isAdmin: true)))); // no self-promotion

        await Service(s => s.ChangeOwnPasswordAsync(new ChangeOwnPasswordInput { CurrentPassword = Password, NewPassword = "NewSecret1", ConfirmPassword = "NewSecret1" }));
    }

    [Fact]
    public async Task Admin_cant_remove_their_own_role_and_nothing_is_saved()
    {
        var me = (await Service(s => s.ListAsync(new UserListQuery()))).Single(u => u.Id == _adminId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Service(s => s.UpdateAsync(_adminId, new UserUpdateInput { FullName = "Renamed", Email = me.Email, IsAdmin = false })));

        Assert.Contains("your own account", ex.Message);
        var after = await Service(s => s.GetAsync(_adminId));
        Assert.True(after.IsAdmin);
        Assert.Equal(me.FullName, after.FullName); // the rejected edit didn't partially save
    }

    [Fact]
    public async Task Promotion_and_demotion_apply_immediately_to_an_existing_session()
    {
        await using var api = await TestHost.CreateAsync(db); // real cookie principals (no test user context)
        var user = await TestHost.CreateUserAsync(api, "worker@test.local", Password);
        var session = await TestHost.SignInAsync(api, "worker@test.local", Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await session.GetAsync("/api/users")).StatusCode);

        await Service(s => s.UpdateAsync(user.Id, new UserUpdateInput { FullName = user.FullName, Email = user.Email, IsAdmin = true }));
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/users")).StatusCode); // no re-login needed

        await Service(s => s.UpdateAsync(user.Id, new UserUpdateInput { FullName = user.FullName, Email = user.Email, IsAdmin = false }));
        Assert.Equal(HttpStatusCode.Forbidden, (await session.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_change_project_statuses_through_the_api()
    {
        await using var api = await TestHost.CreateAsync(db); // real cookie principals (no test user context)
        await TestHost.CreateUserAsync(api, "worker@test.local", Password);
        var user = await TestHost.SignInAsync(api, "worker@test.local", Password);
        var admin = await TestHost.SignInAsync(api);
        var body = new { name = "Waiting", color = "#123456", isActive = true, isClosed = false };

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/project-statuses")).StatusCode); // reading is fine
        var forbidden = await user.PostAsJsonAsync("/api/project-statuses", body);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("Only administrators can change project statuses", await forbidden.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/project-statuses/1/default", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/project-statuses/1")).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/project-statuses", body)).StatusCode);
    }

    [Fact]
    public async Task Startup_seeding_restores_an_admin_when_none_is_left()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.RemoveFromRoleAsync((await users.FindByIdAsync(_adminId))!, Roles.Admin);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DbInitializer>().SeedAdminAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await users.IsInRoleAsync((await users.FindByIdAsync(_adminId))!, Roles.Admin));
        }
    }

    private static UserUpdateInput Update(UserListItem user, bool isAdmin) =>
        new() { FullName = user.FullName, Email = user.Email, IsAdmin = isAdmin };

    private Task<UserListItem> CreateAsync(string email, bool isAdmin = false) =>
        Service(s => s.CreateAsync(new UserCreateInput { FullName = "Test " + email, Email = email, Password = Password, ConfirmPassword = Password, IsAdmin = isAdmin }));

    private async Task<T> Service<T>(Func<IUserAdminService, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IUserAdminService>());
    }

    private sealed class TestUserContext : IUserContext
    {
        private ClaimsPrincipal _principal = new(new ClaimsIdentity());

        public void As(string userId) =>
            _principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"));

        public Task<ClaimsPrincipal> GetPrincipalAsync() => Task.FromResult(_principal);
    }
}
