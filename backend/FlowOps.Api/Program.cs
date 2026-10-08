using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using FlowOps.Api.Email;
using FlowOps.Api.Organizations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IEmailDelivery, SmtpEmailDelivery>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<FlowOps.Api.Forms.LocalFileStorage>();
builder.Services.AddScoped<IAuthorizationHandler, OrganizationPermissionHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in new[] { Permissions.StudioAccess, Permissions.PortalAccess, Permissions.MembersManage })
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
            .AddRequirements(new OrganizationPermissionRequirement(permission)));
});

var connectionString = builder.Configuration.GetConnectionString("FlowOps");
if (string.IsNullOrWhiteSpace(connectionString))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("ConnectionStrings:FlowOps is required outside Development.");

    var password = builder.Configuration["FLOWOPS_DB_PASSWORD"]
        ?? LocalDevelopmentSecrets.ReadDatabasePassword(builder.Environment)
        ?? "";
    connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = builder.Configuration["FLOWOPS_DB_HOST"] ?? builder.Configuration["Database:Host"] ?? "127.0.0.1",
        Port = int.TryParse(builder.Configuration["FLOWOPS_DB_PORT"] ?? builder.Configuration["Database:Port"], out var port) ? port : 5432,
        Database = builder.Configuration["FLOWOPS_DB_NAME"] ?? builder.Configuration["Database:Name"] ?? "flowops",
        Username = builder.Configuration["FLOWOPS_DB_USER"] ?? builder.Configuration["Database:User"] ?? "flowops",
        Password = password
    }.ConnectionString;
}
builder.Services.AddDbContext<FlowOpsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddIdentity<FlowUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = false;
    options.SignIn.RequireConfirmedEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<FlowOpsDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    // Revocation, expiry and disabled users are enforced per request by the BrowserSession check below,
    // so the stamp only needs periodic revalidation.
    options.ValidationInterval = TimeSpan.FromMinutes(1);
    options.OnRefreshingPrincipal = context =>
    {
        var sessionClaim = context.CurrentPrincipal?.FindFirst(AuthEndpoints.SessionClaim);
        if (sessionClaim is not null && context.NewPrincipal?.Identity is ClaimsIdentity identity)
            identity.AddClaim(sessionClaim);
        return Task.CompletedTask;
    };
});
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "FlowOps.Session";
    options.CookieManager = new SurfaceCookieManager();
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "FlowOps.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
// Browsers reach the API through the Next.js proxy, so without forwarded headers every client shares the
// proxy's IP and its rate-limit bucket. Next.js passes a client-supplied X-Forwarded-For through unchanged,
// so only enable this when an edge reverse proxy in front of Next.js sets the header. See docs/SECURITY.md.
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
{
    if (!configuration.GetValue("ForwardedHeaders:Enabled", false)) { options.ForwardedHeaders = ForwardedHeaders.None; return; }
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:AuthPermitLimit", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "FlowOps API v1");
        options.UseRequestInterceptor(string.Join(" ",
            "(request) => {",
            "request.credentials = 'same-origin';",
            "if (request.url.includes('/api/') && !['GET', 'HEAD', 'OPTIONS'].includes((request.method ?? 'GET').toUpperCase())) {",
            "return fetch('/api/auth/csrf', { credentials: 'same-origin' }).then(response => {",
            "if (!response.ok) throw new Error('Unable to fetch CSRF token');",
            "return response.json();",
            "}).then(token => {",
            "request.headers = request.headers ?? {};",
            "request.headers['X-CSRF-TOKEN'] = token.requestToken;",
            "return request;",
            "});",
            "}",
            "return request;",
            "}"));
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var claim = context.User.FindFirstValue(AuthEndpoints.SessionClaim);
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var sessionId) || userId is null)
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }
        else
        {
            var db = context.RequestServices.GetRequiredService<FlowOpsDbContext>();
            var session = await db.BrowserSessions.FirstOrDefaultAsync(x => x.Id == sessionId
                && x.UserId == userId && x.RevokedAt == null);
            var userEnabled = session is not null && session.ExpiresAt > DateTimeOffset.UtcNow
                && await db.Users.AnyAsync(x => x.Id == userId && !x.IsDisabled);
            if (!userEnabled)
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
            else context.Items[AuthEndpoints.SessionItem] = session;
        }
    }
    await next(context);
});
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.GetEndpoint()?.Metadata.GetMetadata<FlowOps.Api.Forms.UploadRequestLimit>() is { } uploadLimit)
    {
        var sizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = uploadLimit.MaxBytes;
        if (context.Request.ContentLength > uploadLimit.MaxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = "Files must total 20 MB or less." });
            return;
        }
    }
    if (context.Request.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        !HttpMethods.IsOptions(context.Request.Method))
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid CSRF token." });
            return;
        }
    }
    await next(context);
});

app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
FlowOps.Api.Forms.FormEndpoints.MapFormEndpoints(app);
app.MapGet("/api/health", () => Results.Ok(new ApiHealthResponse("Healthy", "FlowOps.Api")))
    .WithName("GetApiHealth")
    .WithSummary("Check the FlowOps API status")
    .WithTags("Health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Logger.LogInformation("FlowOps API starting in {Environment}", app.Environment.EnvironmentName);
app.Run();

internal sealed record ApiHealthResponse(string Status, string Service);

public partial class Program;
