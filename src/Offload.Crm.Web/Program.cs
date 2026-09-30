using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using Offload.Crm.Application;
using Offload.Crm.Application.Abstractions;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Infrastructure;
using Offload.Crm.Infrastructure.Persistence;
using Offload.Crm.Infrastructure.Seeding;
using Offload.Crm.Web.Api;
using Offload.Crm.Web.Auth;
using Offload.Crm.Web.Components;
using Offload.Crm.Web.Localization;

var builder = WebApplication.CreateBuilder(args);

// D-28/D-29: formatting is always Slovak (sk-SK, EUR); the UI language is English or Slovak per user (cookie).
var culture = Offload.Crm.Web.Ui.AppCulture.Culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(AppLanguages.Default);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
});
TranslatingSnackbar.Register(builder.Services); // D-29
builder.Services.AddTransient<MudLocalizer, SlovakMudLocalizer>(); // D-29: MudBlazor's own labels

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

// Identity (D-09): cookie auth, email as user name, no public registration.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // BUG-031: the user name is the e-mail, whose format is governed by the app's EmailFormat rule (D-20.8), so
        // Identity's stricter default character list (no ' or diacritics) must not reject it. Empty = any character.
        options.User.AllowedUserNameCharacters = string.Empty;
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager<AppSignInManager>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

// D-19: sessions are re-validated every minute (stamp + active flag) for cookies and circuits; every service
// call from a circuit is checked too (BUG-003/004).
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = IdentityRevalidatingAuthenticationStateProvider.Interval);
builder.Services.AddScoped<ISecurityStampValidator, AppSecurityStampValidator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CircuitContext>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, CircuitContextHandler>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<CircuitSessionGuard>();
builder.Services.AddScoped<Offload.Crm.Web.Ui.ListLocations>();
builder.Services.AddScoped<Offload.Crm.Web.Settings.AppInfoService>();
builder.Services.AddScoped<IAppDbContextFactory, GuardedDbContextFactory>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/account/login";
    options.Cookie.Name = "OffloadCrm.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);

    // The API answers 401/403 instead of redirecting to the login page (T-04).
    options.Events.OnRedirectToLogin = context => ApiAwareRedirect(context, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context, StatusCodes.Status403Forbidden);
});
builder.Services.AddAuthorization(options => options.AddAppPolicies());
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ActiveUserHandler>();
builder.Services.AddScoped<IUserContext, UserContext>();
builder.Services.AddSingleton<SignInTickets>();
builder.Services.AddScoped<Offload.Crm.Application.Users.IUserAdminService, Offload.Crm.Web.Users.UserAdminService>();

// D-27: roles. Admin-only operations are checked server-side (UI and API), not just hidden in the UI.
builder.Services.AddScoped<AdminGuard>();
builder.Services.AddScoped<Offload.Crm.Application.ProjectStatuses.ProjectStatusService>();
builder.Services.AddScoped<Offload.Crm.Application.ProjectStatuses.IProjectStatusService, AdminProjectStatusService>();

builder.Services.AddProblemDetails();

// T-27 / D-25: OpenAPI (UI in Development only) and /health.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Offload CRM API", Version = "v1" });
    options.OperationFilter<ApiQueryOperationFilter>();
    options.CustomSchemaIds(type => type.FullName?.Replace("+", ".", StringComparison.Ordinal));
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// Binding errors throw so ApiErrorMiddleware can answer with a readable ProblemDetails (BUG-008).
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

// D-29: UI language from the culture cookie, then the browser (Accept-Language), else English. The formatting
// culture never changes. A Blazor circuit keeps the language of the request that started it.
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture.Name, AppLanguages.Default),
    SupportedCultures = [culture],
    SupportedUICultures = AppLanguages.All.Select(l => CultureInfo.GetCultureInfo(l.Code)).ToList(),
    RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()],
});

// API: ProblemDetails for exceptions and for empty error responses such as 401 (D-20.7).
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    branch =>
    {
        branch.UseMiddleware<ApiErrorMiddleware>();
        branch.UseStatusCodePages();
    });

// Friendly 404 page for unknown page URLs; the API keeps plain status codes.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/status/{0}"));

app.UseStaticFiles();

// Explicit routing after the status-code middleware so a re-executed request gets re-routed.
app.UseRouting();

// Pages must not be served from the browser cache (e.g. back button after logout).
app.Use((context, next) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Pragma = "no-cache";
    return next(context);
});

app.UseAuthentication();

// Unknown /api routes: 401 for anonymous callers (don't reveal the API surface), otherwise the empty 404 becomes a
// ProblemDetails via UseStatusCodePages. Wrong verb / content type get routing's 405 / 415.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        && context.GetEndpoint() is null
        && context.User.Identity?.IsAuthenticated != true)
    {
        await context.ChallengeAsync();
        return;
    }

    await next(context);
});

app.UseAuthorization();
app.UseAntiforgery();

app.MapApi();
app.MapHealthChecks("/health").AllowAnonymous();

// D-29: remembers the UI language for a year and returns to the (local) page the user was on.
app.MapGet("/culture/set", (string? culture, string? redirectUri, HttpContext context) =>
    {
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(Offload.Crm.Web.Ui.AppCulture.Name, AppLanguages.Normalize(culture))),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
        return Results.LocalRedirect(SafeLocalUrl(redirectUri));
    })
    .AllowAnonymous()
    .ExcludeFromDescription();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "Offload CRM API");
}


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await InitializeDatabaseAsync(app);

app.Run();

// Only same-app paths: no "//host", "/\host" or control characters (browsers drop tabs/newlines, e.g. "/%09/host").
static string SafeLocalUrl(string? url) =>
    url is { Length: > 0 } && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal)
    && !url.StartsWith("/\\", StringComparison.Ordinal) && !url.Any(char.IsControl)
        ? url
        : "/";

static Task ApiAwareRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> context, int apiStatus)
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = apiStatus;
    }
    else
    {
        context.Response.Redirect(context.RedirectUri);
    }

    return Task.CompletedTask;
}

// D-01: in Development migrations + seed run on startup; elsewhere only with Database:MigrateOnStartup=true.
static async Task InitializeDatabaseAsync(WebApplication app)
{
    var migrate = app.Environment.IsDevelopment()
        || app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup;
    if (!migrate)
    {
        return;
    }

    // D-26: every configured database (production + test) is kept on the current schema. When a test database
    // exists, demo data goes only there — production gets reference data and the admin, never demo records.
    var databases = app.Services.GetRequiredService<DatabaseSelector>();
    foreach (var target in databases.ConfiguredTargets)
    {
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<DatabaseScope>().Target = target;
        var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
        var demoData = app.Environment.IsDevelopment() && (target == DatabaseTarget.Test || !databases.CanSwitch);
        await initializer.InitializeAsync(migrate: true, seedDemoData: demoData);
    }
}

/// <summary>Entry point; public for WebApplicationFactory-based integration tests.</summary>
public partial class Program
{
}
