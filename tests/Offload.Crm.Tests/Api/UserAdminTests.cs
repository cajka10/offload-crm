using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Users;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Tests.Infrastructure;
using Offload.Crm.Web.Auth;

namespace Offload.Crm.Tests.Api;

/// <summary>T-24/T-28: user rules and their effect on sessions (D-24).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class UserAdminTests(DatabaseFixture db) : IAsyncLifetime
{
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

    private async Task<T> WithService<T>(Func<IUserAdminService, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IUserAdminService>());
    }

    private Task WithService(Func<IUserAdminService, Task> action) => WithService(async s =>
    {
        await action(s);
        return true;
    });

    private Task<UserListItem> CreateAsync(string email, string password = "Secret123") =>
        WithService(s => s.CreateAsync(new UserCreateInput { FullName = "Test " + email, Email = email, Password = password, ConfirmPassword = password }));

    [Fact]
    public async Task Create_validates_email_uniqueness_and_password_policy()
    {
        var created = await CreateAsync("jan@test.local");
        Assert.True(created.IsActive);

        var duplicate = await Assert.ThrowsAsync<AppValidationException>(() => CreateAsync("JAN@test.local"));
        Assert.Contains("Email", duplicate.Errors.Keys);

        var weak = await Assert.ThrowsAsync<AppValidationException>(() => CreateAsync("weak@test.local", "short"));
        Assert.Contains(nameof(UserCreateInput.Password), weak.Errors.Keys);

        var invalid = await Assert.ThrowsAsync<AppValidationException>(() => CreateAsync("not-an-email"));
        Assert.Contains(nameof(UserCreateInput.Email), invalid.Errors.Keys);

        var mismatch = await Assert.ThrowsAsync<AppValidationException>(() => WithService(s =>
            s.CreateAsync(new UserCreateInput { FullName = "X", Email = "x@test.local", Password = "Secret123", ConfirmPassword = "Other123" })));
        Assert.Contains(nameof(UserCreateInput.ConfirmPassword), mismatch.Errors.Keys);
    }

    [Fact]
    public async Task Edit_email_to_an_existing_one_is_rejected()
    {
        var jan = await CreateAsync("jan@test.local");
        await CreateAsync("eva@test.local");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            WithService(s => s.UpdateAsync(jan.Id, new UserUpdateInput { FullName = "Jan", Email = "Eva@test.local" })));

        Assert.Contains("Email", ex.Errors.Keys);
        var renamed = await WithService(s => s.UpdateAsync(jan.Id, new UserUpdateInput { FullName = "Jan N", Email = "jan.n@test.local" }));
        Assert.Equal("jan.n@test.local", renamed.Email);
        await TestHost.SignInAsync(_factory, "jan.n@test.local", "Secret123"); // the login name changed too
    }

    [Fact]
    public async Task Cant_deactivate_yourself()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() => WithService(s => s.DeactivateAsync(_adminId)));

        Assert.Equal("You can't deactivate your own account.", ex.Message);
    }

    [Fact]
    public async Task Deactivation_ends_sessions_blocks_sign_in_and_reports_impact()
    {
        var jan = await CreateAsync("jan@test.local");
        var customer = await db.AddCustomerAsync("C");
        var project = await db.AddProjectAsync(customer.Id);
        await db.AddTaskAsync(project.Id, "Open", assigneeId: jan.Id);
        await db.AddTaskAsync(project.Id, "Done", Domain.Enums.TaskItemStatus.Done, assigneeId: jan.Id);
        var session = await TestHost.SignInAsync(_factory, "jan@test.local", "Secret123");
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/customers")).StatusCode);

        var impact = await WithService(s => s.GetDeactivationImpactAsync(jan.Id));
        await WithService(s => s.DeactivateAsync(jan.Id));

        Assert.Equal(new DeactivationImpact(1, 0), impact);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await TryLoginAsync("jan@test.local", "Secret123")); // 200 = the form again, no redirect

        await WithService(s => s.ReactivateAsync(jan.Id));
        await TestHost.SignInAsync(_factory, "jan@test.local", "Secret123");
    }

    [Fact]
    public async Task Reset_password_ends_sessions_clears_lockout_and_new_password_works()
    {
        var jan = await CreateAsync("jan@test.local");
        var session = await TestHost.SignInAsync(_factory, "jan@test.local", "Secret123");
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByIdAsync(jan.Id))!, DateTimeOffset.UtcNow.AddMinutes(5));
        }

        var weak = await Assert.ThrowsAsync<AppValidationException>(() =>
            WithService(s => s.ResetPasswordAsync(jan.Id, new PasswordResetInput { Password = "weak", ConfirmPassword = "weak" })));
        Assert.Contains(nameof(PasswordResetInput.Password), weak.Errors.Keys);

        await WithService(s => s.ResetPasswordAsync(jan.Id, new PasswordResetInput { Password = "NewSecret1", ConfirmPassword = "NewSecret1" }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/customers")).StatusCode);
        await TestHost.SignInAsync(_factory, "jan@test.local", "NewSecret1");
    }

    [Fact]
    public async Task Change_own_password_keeps_the_current_session_and_ends_the_others()
    {
        var jan = await CreateAsync("jan@test.local");
        var current = await TestHost.SignInAsync(_factory, "jan@test.local", "Secret123");
        var other = await TestHost.SignInAsync(_factory, "jan@test.local", "Secret123");
        _context.As(jan.Id);

        var wrong = await Assert.ThrowsAsync<AppValidationException>(() => WithService(s =>
            s.ChangeOwnPasswordAsync(new ChangeOwnPasswordInput { CurrentPassword = "nope", NewPassword = "NewSecret1", ConfirmPassword = "NewSecret1" })));
        Assert.Contains(nameof(ChangeOwnPasswordInput.CurrentPassword), wrong.Errors.Keys);

        var ticket = await WithService(s =>
            s.ChangeOwnPasswordAsync(new ChangeOwnPasswordInput { CurrentPassword = "Secret123", NewPassword = "NewSecret1", ConfirmPassword = "NewSecret1" }));

        // The tab that changed the password exchanges the one-time ticket for a fresh cookie.
        var refresh = await current.GetAsync($"/account/refresh?ticket={ticket}&ReturnUrl=%2Fcustomers");
        Assert.Equal(HttpStatusCode.Redirect, refresh.StatusCode);
        Assert.EndsWith("/customers", refresh.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/customers")).StatusCode);

        // Tickets are single use.
        var reuse = await other.GetAsync($"/account/refresh?ticket={ticket}");
        Assert.Contains("expired=1", reuse.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Manage_users_policy_is_enforced()
    {
        var jan = await CreateAsync("jan@test.local");
        await WithService(s => s.DeactivateAsync(jan.Id));

        _context.As(jan.Id); // an inactive user
        await Assert.ThrowsAsync<ForbiddenException>(() => WithService(s => s.ListAsync(new UserListQuery())));

        _context.Anonymous();
        await Assert.ThrowsAsync<ForbiddenException>(() => WithService(s => s.ListAsync(new UserListQuery())));
    }

    [Fact]
    public async Task List_filters_and_sorts_active_first()
    {
        var zed = await CreateAsync("zed@test.local");
        await CreateAsync("amy@test.local");
        await WithService(s => s.DeactivateAsync(zed.Id));

        var all = await WithService(s => s.ListAsync(new UserListQuery()));
        var inactive = await WithService(s => s.ListAsync(new UserListQuery { Active = false }));
        var search = await WithService(s => s.ListAsync(new UserListQuery { Search = "AMY" }));

        Assert.Equal("zed@test.local", all[^1].Email);
        Assert.True(all.Single(u => u.Id == _adminId).IsCurrentUser);
        Assert.Equal([zed.Id], inactive.Select(u => u.Id));
        Assert.Equal(["amy@test.local"], search.Select(u => u.Email));
    }

    private async Task<HttpStatusCode> TryLoginAsync(string email, string password)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var page = await client.GetStringAsync("/account/login");
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = TestHost.Token(page),
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }));
        return response.StatusCode;
    }

    private sealed class TestUserContext : IUserContext
    {
        private ClaimsPrincipal _principal = new(new ClaimsIdentity());

        public void As(string userId) =>
            _principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"));

        public void Anonymous() => _principal = new ClaimsPrincipal(new ClaimsIdentity());

        public Task<ClaimsPrincipal> GetPrincipalAsync() => Task.FromResult(_principal);
    }
}
