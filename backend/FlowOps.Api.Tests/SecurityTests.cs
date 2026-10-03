using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using FlowOps.Api.Data;
using FlowOps.Api.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace FlowOps.Api.Tests;

public sealed class SecurityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<FlowOpsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<FlowOpsDbContext>>();
                services.AddDbContext<FlowOpsDbContext>(options => options.UseSqlite(_connection));
                services.RemoveAll<IEmailDelivery>();
                services.AddSingleton<IEmailDelivery, TestEmailDelivery>();
            });
        });
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true
    });

    private static async Task<HttpResponseMessage> Mutate(HttpClient client, HttpMethod method,
        string path, object? body = null, bool addCsrf = true)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (addCsrf)
        {
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        }
        return await client.SendAsync(request);
    }

    private async Task RegisterAndLogin(HttpClient client, string email, string surface = "Studio")
    {
        client.DefaultRequestHeaders.Remove("X-FlowOps-Surface");
        client.DefaultRequestHeaders.Add("X-FlowOps-Surface", surface);
        var register = await Mutate(client, HttpMethod.Post, "/api/auth/register",
            new { email, password = "SecurePass123!", displayName = email.Split('@')[0], surface });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Mutate(client, HttpMethod.Post, "/api/auth/login",
                new { email, password = "SecurePass123!", surface })).StatusCode);
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<FlowUser>>();
            var user = (await FlowOps.Api.Auth.AuthEndpoints.FindAccount(users, email, surface))!;
            token = await users.GenerateEmailConfirmationTokenAsync(user);
        }
        Assert.Equal(HttpStatusCode.NoContent,
            (await Mutate(client, HttpMethod.Post, "/api/auth/email/confirm", new { email, token, surface })).StatusCode);
        var login = await Mutate(client, HttpMethod.Post, "/api/auth/login",
            new { email, password = "SecurePass123!", surface });
        Assert.True(login.StatusCode == HttpStatusCode.OK,
            $"Login returned {login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
    }

    private static async Task<Guid> CreateOrganization(HttpClient client, string name)
    {
        var response = await Mutate(client, HttpMethod.Post, "/api/organizations", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static string AuthenticatorCode(string key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in key.ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            Assert.InRange(value, 0, 31);
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 15;
        var number = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (number % 1_000_000).ToString("D6");
    }

    [Fact]
    public async Task Same_email_accounts_have_independent_credentials_tokens_and_access()
    {
        const string email = "separate@example.test";
        using var studio = NewClient();
        using var portal = NewClient();
        using var anonymous = NewClient();
        await RegisterAndLogin(studio, email, "Studio");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Mutate(anonymous, HttpMethod.Post, "/api/auth/login",
            new { email, password = "SecurePass123!", surface = "Portal" })).StatusCode);
        await RegisterAndLogin(portal, email, "Portal");
        var studioMe = await studio.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var portalMe = await portal.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.NotEqual(studioMe.GetProperty("id").GetString(), portalMe.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/auth/studio-onboarding")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await studio.GetAsync("/api/portal/access")).StatusCode);
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<FlowUser>>();
            token = await users.GeneratePasswordResetTokenAsync((await FlowOps.Api.Auth.AuthEndpoints.FindAccount(users, email, "Studio"))!);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(anonymous, HttpMethod.Post, "/api/auth/password/reset",
            new { email, token, newPassword = "ChangedSecure123!", surface = "Portal" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(anonymous, HttpMethod.Post, "/api/auth/password/reset",
            new { email, token, newPassword = "ChangedSecure123!", surface = "Studio" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Mutate(anonymous, HttpMethod.Post, "/api/auth/login",
            new { email, password = "ChangedSecure123!", surface = "Portal" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Mutate(anonymous, HttpMethod.Post, "/api/auth/login",
            new { email, password = "SecurePass123!", surface = "Portal" })).StatusCode);
    }

    [Fact]
    public async Task Studio_and_portal_sessions_coexist_in_one_browser()
    {
        using var browser = NewClient();
        await RegisterAndLogin(browser, "browser@example.test", "Studio");
        var studio = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me");
        await RegisterAndLogin(browser, "browser@example.test", "Portal");
        var portal = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.NotEqual(studio.GetProperty("id").GetString(), portal.GetProperty("id").GetString());
        browser.DefaultRequestHeaders.Remove("X-FlowOps-Surface");
        browser.DefaultRequestHeaders.Add("X-FlowOps-Surface", "Studio");
        var stillStudio = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(studio.GetProperty("id").GetString(), stillStudio.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(browser, HttpMethod.Post, "/api/auth/logout")).StatusCode);
        browser.DefaultRequestHeaders.Remove("X-FlowOps-Surface");
        browser.DefaultRequestHeaders.Add("X-FlowOps-Surface", "Portal");
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_and_missing_csrf_requests_are_rejected()
    {
        using var client = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/organizations")).StatusCode);
        var response = await Mutate(client, HttpMethod.Post, "/api/auth/register",
            new { email = "csrf@example.test", password = "SecurePass123!", displayName = "CSRF" },
            addCsrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Email_links_return_to_the_requesting_surface()
    {
        using var client = NewClient();
        var mail = (TestEmailDelivery)_factory.Services.GetRequiredService<IEmailDelivery>();
        const string email = "surface@example.test";
        Assert.Equal(HttpStatusCode.Created,
            (await Mutate(client, HttpMethod.Post, "/api/auth/register",
                new { email, password = "SecurePass123!", displayName = "Surface", surface = "Studio" })).StatusCode);
        Assert.StartsWith("http://localhost:3000/confirm-email?", mail.Messages.Last().Body.Split('\n')[1]);

        Assert.Equal(HttpStatusCode.Accepted,
            (await Mutate(client, HttpMethod.Post, "/api/auth/email/resend",
                new { email, surface = "Portal" })).StatusCode);
        Assert.StartsWith("http://localhost:3000/confirm-email?", mail.Messages.Last().Body.Split('\n')[1]);

        Assert.Equal(HttpStatusCode.Accepted,
            (await Mutate(client, HttpMethod.Post, "/api/auth/email/resend",
                new { email, surface = "Studio" })).StatusCode);
        Assert.StartsWith("http://localhost:3000/confirm-email?", mail.Messages.Last().Body.Split('\n')[1]);

        var confirmationLink = new Uri(mail.Messages.Last().Body.Split('\n')[1]);
        var token = QueryHelpers.ParseQuery(confirmationLink.Query)["token"].ToString();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Mutate(client, HttpMethod.Post, "/api/auth/email/confirm", new { email, token = "invalid", surface = "Studio" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Mutate(client, HttpMethod.Post, "/api/auth/email/confirm", new { email, token, surface = "Studio" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Mutate(client, HttpMethod.Post, "/api/auth/email/confirm", new { email, token, surface = "Studio" })).StatusCode);
        var emailCount = mail.Messages.Count;
        Assert.Equal(HttpStatusCode.Accepted,
            (await Mutate(client, HttpMethod.Post, "/api/auth/email/resend", new { email })).StatusCode);
        Assert.Equal(emailCount, mail.Messages.Count);
        Assert.Equal(HttpStatusCode.Accepted,
            (await Mutate(client, HttpMethod.Post, "/api/auth/password/reset-request",
                new { email, surface = "Studio" })).StatusCode);
        Assert.StartsWith("http://localhost:3000/reset-password?", mail.Messages.Last().Body.Split('\n')[1]);
    }

    [Fact]
    public async Task Studio_signup_can_onboard_without_granting_portal_users_studio_access()
    {
        using var portal = NewClient();
        using var studio = NewClient();
        await RegisterAndLogin(portal, "portal-gate@example.test", "Portal");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Mutate(studio, HttpMethod.Post, "/api/auth/login",
                new { email = "portal-gate@example.test", password = "SecurePass123!", surface = "Studio" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await studio.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/auth/studio-onboarding")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Mutate(portal, HttpMethod.Post, "/api/organizations", new { name = "Unauthorized workspace" })).StatusCode);

        await RegisterAndLogin(studio, "studio-gate@example.test");
        Assert.Equal(HttpStatusCode.OK,
            (await Mutate(studio, HttpMethod.Post, "/api/auth/login",
                new { email = "studio-gate@example.test", password = "SecurePass123!", surface = "Studio" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await studio.GetAsync("/api/auth/studio-onboarding")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await studio.GetAsync("/api/studio/access")).StatusCode);
        var organizationId = await CreateOrganization(studio, "Studio Gate Workspace");
        Assert.Equal(HttpStatusCode.Forbidden, (await studio.GetAsync("/api/studio/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await studio.GetAsync($"/api/organizations/{organizationId}/members")).StatusCode);

        string code;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<FlowUser>>();
            var user = (await users.FindByEmailAsync("studio-gate@example.test"))!;
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            code = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
        }
        Assert.Equal(HttpStatusCode.OK,
            (await Mutate(studio, HttpMethod.Post, "/api/auth/login",
                new { email = "studio-gate@example.test", password = "SecurePass123!", code, surface = "Studio" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await studio.GetAsync("/api/studio/access")).StatusCode);
    }

    [Fact]
    public async Task Organization_ids_cannot_cross_tenant_boundaries()
    {
        using var alice = NewClient();
        using var bob = NewClient();
        await RegisterAndLogin(alice, "alice@example.test");
        await RegisterAndLogin(bob, "bob@example.test");
        var aliceOrg = await CreateOrganization(alice, "Alice Workspace");
        var bobOrg = await CreateOrganization(bob, "Bob Workspace");

        Assert.Equal(HttpStatusCode.NotFound,
            (await Mutate(alice, HttpMethod.Post, $"/api/organizations/{bobOrg}/activate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await alice.GetAsync($"/api/organizations/{bobOrg}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Mutate(bob, HttpMethod.Post, $"/api/organizations/{aliceOrg}/members",
                new { email = "alice@example.test", role = "Admin" })).StatusCode);
    }

    [Fact]
    public async Task Privileged_access_requires_mfa_and_revoked_session_fails()
    {
        using var client = NewClient();
        await RegisterAndLogin(client, "owner@example.test");
        await CreateOrganization(client, "Owner Workspace");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/studio/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/studio/access")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Mutate(client, HttpMethod.Post, "/api/auth/sessions/revoke-all")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Recovery_code_login_satisfies_mfa_once()
    {
        using var client = NewClient();
        await RegisterAndLogin(client, "mfa@example.test");
        await CreateOrganization(client, "MFA Workspace");
        string code;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<FlowUser>>();
            var user = (await users.FindByEmailAsync("mfa@example.test"))!;
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            code = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
        }

        var failedMfa = await Mutate(client, HttpMethod.Post, "/api/auth/login",
            new { email = "mfa@example.test", password = "SecurePass123!", code = "invalid", surface = "Studio" });
        Assert.Equal(HttpStatusCode.Unauthorized, failedMfa.StatusCode);
        Assert.Contains("Authenticator or recovery code is incorrect", await failedMfa.Content.ReadAsStringAsync());
        var challenge = await Mutate(client, HttpMethod.Post, "/api/auth/login",
            new { email = "mfa@example.test", password = "SecurePass123!", surface = "Studio" });
        Assert.Equal(HttpStatusCode.Accepted, challenge.StatusCode);
        var success = await Mutate(client, HttpMethod.Post, "/api/auth/login",
            new { email = "mfa@example.test", password = "SecurePass123!", code, surface = "Studio" });
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<FlowUser>>();
            Assert.Equal(0, (await users.FindByEmailAsync("mfa@example.test"))!.AccessFailedCount);
        }
        var meAfterMfa = await client.GetAsync("/api/auth/me");
        Assert.True(meAfterMfa.StatusCode == HttpStatusCode.OK,
            $"Me after MFA: {meAfterMfa.StatusCode} {await meAfterMfa.Content.ReadAsStringAsync()}");
        var studioAccess = await client.GetAsync("/api/studio/access");
        Assert.True(studioAccess.StatusCode == HttpStatusCode.OK,
            $"Studio: {studioAccess.StatusCode} {await studioAccess.Content.ReadAsStringAsync()}");

        var reused = await Mutate(client, HttpMethod.Post, "/api/auth/login",
            new { email = "mfa@example.test", password = "SecurePass123!", code, surface = "Studio" });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Member_permissions_follow_active_organization_and_removal()
    {
        using var owner = NewClient();
        using var member = NewClient();
        await RegisterAndLogin(owner, "teamowner@example.test");
        await RegisterAndLogin(member, "teammember@example.test", "Portal");
        var organizationId = await CreateOrganization(owner, "Team Workspace");

        string code;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<FlowUser>>();
            var ownerUser = (await users.FindByEmailAsync("teamowner@example.test"))!;
            await users.SetTwoFactorEnabledAsync(ownerUser, true);
            code = (await users.GenerateNewTwoFactorRecoveryCodesAsync(ownerUser, 1))!.Single();
        }
        Assert.Equal(HttpStatusCode.OK,
            (await Mutate(owner, HttpMethod.Post, "/api/auth/login",
                new { email = "teamowner@example.test", password = "SecurePass123!", code, surface = "Studio" })).StatusCode);
        var invalidEmail = await Mutate(owner, HttpMethod.Post, $"/api/organizations/{organizationId}/members",
            new { email = "invalid-email", role = "Member" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);
        Assert.Contains("Enter a valid email address", await invalidEmail.Content.ReadAsStringAsync());
        var missingAccount = await Mutate(owner, HttpMethod.Post, $"/api/organizations/{organizationId}/members",
            new { email = "missing@example.test", role = "Member" });
        Assert.Equal(HttpStatusCode.NotFound, missingAccount.StatusCode);
        Assert.Contains("No confirmed Portal account", await missingAccount.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created,
            (await Mutate(owner, HttpMethod.Post, $"/api/organizations/{organizationId}/members",
                new { email = "teammember@example.test", role = "Member" })).StatusCode);

        var secondOrganizationId = await CreateOrganization(owner, "Separate Workspace");
        var currentUser = await owner.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(secondOrganizationId,
            currentUser.GetProperty("activeOrganization").GetProperty("organizationId").GetGuid());
        var secondMembers = await owner.GetFromJsonAsync<JsonElement>($"/api/organizations/{secondOrganizationId}/members");
        Assert.Equal(1, secondMembers.GetArrayLength());
        Assert.Equal("Owner", secondMembers[0].GetProperty("role").GetString());
        var portalOrganizations = await member.GetFromJsonAsync<JsonElement>("/api/organizations");
        Assert.Single(portalOrganizations.EnumerateArray());
        Assert.Equal(organizationId, portalOrganizations[0].GetProperty("organizationId").GetGuid());
        Assert.Equal(HttpStatusCode.NoContent,
            (await Mutate(owner, HttpMethod.Post, $"/api/organizations/{organizationId}/activate")).StatusCode);
        var firstMembers = await owner.GetFromJsonAsync<JsonElement>($"/api/organizations/{organizationId}/members");
        Assert.Equal(2, firstMembers.GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent,
            (await Mutate(member, HttpMethod.Post, $"/api/organizations/{organizationId}/activate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/portal/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/studio/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.GetAsync($"/api/organizations/{organizationId}/members")).StatusCode);

        using var scopeAfter = _factory.Services.CreateScope();
        var db = scopeAfter.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        var memberUser = await db.Users.SingleAsync(x => x.Email == "teammember@example.test");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Mutate(owner, HttpMethod.Delete,
                $"/api/organizations/{organizationId}/members/{memberUser.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/portal/access")).StatusCode);
        Assert.True(await db.AuditEvents.AnyAsync(x => x.OrganizationId == organizationId
            && x.Action == "organization.member_removed"));
    }

    [Fact]
    public async Task Authenticator_setup_keeps_current_session_valid()
    {
        using var client = NewClient();
        await RegisterAndLogin(client, "setup@example.test");
        var setup = await client.GetAsync("/api/auth/mfa/setup");
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var body = await setup.Content.ReadFromJsonAsync<JsonElement>();
        var key = body.GetProperty("sharedKey").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(key));
        var uri = new Uri(body.GetProperty("authenticatorUri").GetString()!);
        Assert.Equal("otpauth", uri.Scheme);
        Assert.Equal(key, QueryHelpers.ParseQuery(uri.Query)["secret"].ToString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Mutate(client, HttpMethod.Post, "/api/auth/mfa/enable", new { code = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var enable = await Mutate(client, HttpMethod.Post, "/api/auth/mfa/enable",
            new { code = AuthenticatorCode(key) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var enabled = await enable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, enabled.GetProperty("recoveryCodes").GetArrayLength());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.True(me.GetProperty("mfaSatisfied").GetBoolean());
    }

    [Fact]
    public async Task Draft_forms_require_studio_mfa_and_stay_in_the_active_organization()
    {
        using var studio = NewClient();
        using var portal = NewClient();
        await RegisterAndLogin(studio, "form-owner@example.test");
        await RegisterAndLogin(portal, "form-member@example.test", "Portal");
        var firstOrganization = await CreateOrganization(studio, "First Forms Workspace");
        Assert.Equal(HttpStatusCode.Forbidden, (await studio.GetAsync("/api/forms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(studio, HttpMethod.Post, "/api/forms",
            new { name = "Protected draft" })).StatusCode);
        var setup = await studio.GetFromJsonAsync<JsonElement>("/api/auth/mfa/setup");
        Assert.Equal(HttpStatusCode.OK, (await Mutate(studio, HttpMethod.Post, "/api/auth/mfa/enable",
            new { code = AuthenticatorCode(setup.GetProperty("sharedKey").GetString()!) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(studio, HttpMethod.Post, "/api/forms", new { name = " " })).StatusCode);
        var created = await Mutate(studio, HttpMethod.Post, "/api/forms", new { name = "Expense claim", description = "Draft description" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var formId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Mutate(studio, HttpMethod.Put, $"/api/forms/{formId}", new { name = "Updated expense claim", description = "Updated" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Mutate(studio, HttpMethod.Post,
            $"/api/organizations/{firstOrganization}/members", new { email = "form-member@example.test", role = "Member" })).StatusCode);
        await Mutate(portal, HttpMethod.Post, $"/api/organizations/{firstOrganization}/activate");
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/forms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(portal, HttpMethod.Put, $"/api/forms/{formId}", new { name = "Unauthorized edit" })).StatusCode);
        await CreateOrganization(studio, "Second Forms Workspace");
        Assert.Empty((await studio.GetFromJsonAsync<JsonElement>("/api/forms")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Mutate(studio, HttpMethod.Put, $"/api/forms/{formId}", new { name = "Cross organization edit" })).StatusCode);
        await Mutate(studio, HttpMethod.Post, $"/api/organizations/{firstOrganization}/activate");
        var rows = await studio.GetFromJsonAsync<JsonElement>("/api/forms");
        Assert.Equal("Updated expense claim", Assert.Single(rows.EnumerateArray()).GetProperty("name").GetString());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(x => x.Action == "form.created" && x.OrganizationId == firstOrganization));
        Assert.True(await db.AuditEvents.AnyAsync(x => x.Action == "form.updated" && x.SubjectId == formId.ToString()));
    }

    [Fact]
    public async Task Https_sessions_use_secure_http_only_cookies_for_each_surface()
    {
        foreach (var surface in new[] { "Studio", "Portal" })
        {
            using var client = NewClient();
            var csrf = await client.GetAsync("/api/auth/csrf");
            var cookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie"), value => value.StartsWith("FlowOps.Csrf="));
            Assert.Contains("secure", cookie.ToLowerInvariant());
            Assert.Contains("httponly", cookie.ToLowerInvariant());
            Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
            await RegisterAndLogin(client, "cookie-flags@example.test", surface);
            var login = await Mutate(client, HttpMethod.Post, "/api/auth/login",
                new { email = "cookie-flags@example.test", password = "SecurePass123!", surface });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var sessionCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"),
                value => value.StartsWith($"FlowOps.Session.{surface}="));
            Assert.Contains("secure", sessionCookie.ToLowerInvariant());
            Assert.Contains("httponly", sessionCookie.ToLowerInvariant());
            Assert.Contains("samesite=lax", sessionCookie.ToLowerInvariant());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        }
    }

    [Fact]
    public async Task Admin_can_manage_portal_members_but_cannot_remove_owner_or_assign_admin()
    {
        using var owner = NewClient();
        using var admin = NewClient();
        using var member = NewClient();
        await RegisterAndLogin(owner, "permissions-owner@example.test");
        await RegisterAndLogin(admin, "permissions-admin@example.test");
        await RegisterAndLogin(member, "permissions-member@example.test", "Portal");
        var organizationId = await CreateOrganization(owner, "Permissions Workspace");
        foreach (var client in new[] { owner, admin })
        {
            var setup = await client.GetFromJsonAsync<JsonElement>("/api/auth/mfa/setup");
            Assert.Equal(HttpStatusCode.OK, (await Mutate(client, HttpMethod.Post, "/api/auth/mfa/enable",
                new { code = AuthenticatorCode(setup.GetProperty("sharedKey").GetString()!) })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await Mutate(owner, HttpMethod.Post,
            $"/api/organizations/{organizationId}/members", new { email = "permissions-admin@example.test", role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(admin, HttpMethod.Post,
            $"/api/organizations/{organizationId}/activate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/studio/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Mutate(admin, HttpMethod.Post,
            $"/api/organizations/{organizationId}/members", new { email = "permissions-member@example.test", role = "Member" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(admin, HttpMethod.Post,
            $"/api/organizations/{organizationId}/members", new { email = "permissions-owner@example.test", role = "Admin" })).StatusCode);
        var people = await admin.GetFromJsonAsync<JsonElement>($"/api/organizations/{organizationId}/members");
        var ownerId = people.EnumerateArray().Single(x => x.GetProperty("role").GetString() == "Owner").GetProperty("userId").GetString();
        var memberId = people.EnumerateArray().Single(x => x.GetProperty("role").GetString() == "Member").GetProperty("userId").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(admin, HttpMethod.Delete,
            $"/api/organizations/{organizationId}/members/{ownerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(owner, HttpMethod.Delete,
            $"/api/organizations/{organizationId}/members/{ownerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(admin, HttpMethod.Delete,
            $"/api/organizations/{organizationId}/members/{memberId}")).StatusCode);
        Assert.Empty((await member.GetFromJsonAsync<JsonElement>("/api/organizations")).EnumerateArray());
    }

    [Fact]
    public async Task Expired_session_cannot_access_protected_endpoints()
    {
        using var client = NewClient();
        await RegisterAndLogin(client, "expired@example.test");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOpsDbContext>();
            var session = await db.BrowserSessions.SingleAsync();
            session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/organizations")).StatusCode);
    }

    [Theory]
    [InlineData("Studio")]
    [InlineData("Portal")]
    public async Task Five_failed_passwords_lock_account_until_lockout_expires(string surface)
    {
        using var client = NewClient();
        const string email = "lockout@example.test";
        await RegisterAndLogin(client, email, surface);
        await Mutate(client, HttpMethod.Post, "/api/auth/logout");
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await Mutate(client, HttpMethod.Post, "/api/auth/login",
                new { email, password = "WrongPassword123!", surface });
            Assert.Equal(attempt == 5 ? (HttpStatusCode)423 : HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Equal((HttpStatusCode)423, (await Mutate(client, HttpMethod.Post, "/api/auth/login",
            new { email, password = "SecurePass123!", surface })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<FlowUser>>();
            var user = (await FlowOps.Api.Auth.AuthEndpoints.FindAccount(users, email, surface))!;
            Assert.InRange(user.LockoutEnd!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
            // Advance only the test account's deadline; no wall-clock wait or policy bypass in production.
            Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddSeconds(-1))).Succeeded);
        }
        Assert.Equal(HttpStatusCode.OK, (await Mutate(client, HttpMethod.Post, "/api/auth/login",
            new { email, password = "SecurePass123!", surface })).StatusCode);
    }

    [Fact]
    public async Task Password_reset_revokes_existing_sessions()
    {
        using var client = NewClient();
        await RegisterAndLogin(client, "reset@example.test");
        Assert.Equal(HttpStatusCode.Accepted,
            (await Mutate(client, HttpMethod.Post, "/api/auth/password/reset-request",
                new { email = "reset@example.test", surface = "Studio" })).StatusCode);
        var mail = _factory.Services.GetRequiredService<IEmailDelivery>() as TestEmailDelivery;
        var link = new Uri(mail!.Messages.Last().Body.Split('\n')[1]);
        var token = QueryHelpers.ParseQuery(link.Query)["token"].ToString();
        var weakPassword = await Mutate(client, HttpMethod.Post, "/api/auth/password/reset",
            new { email = "reset@example.test", token, newPassword = "abcdefghijkl", surface = "Studio" });
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        var validation = await weakPassword.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(validation.GetProperty("errors").TryGetProperty("PasswordRequiresDigit", out _));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Mutate(client, HttpMethod.Post, "/api/auth/password/reset",
                new { email = "reset@example.test", token, newPassword = "AnotherSecure123!", surface = "Studio" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Mutate(client, HttpMethod.Post, "/api/auth/login",
                new { email = "reset@example.test", password = "SecurePass123!", surface = "Studio" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Mutate(client, HttpMethod.Post, "/api/auth/login",
                new { email = "reset@example.test", password = "AnotherSecure123!", surface = "Studio" })).StatusCode);
    }
}

public sealed class TestEmailDelivery : IEmailDelivery
{
    public List<(string Address, string Subject, string Body)> Messages { get; } = [];
    public Task SendAsync(string address, string subject, string body)
    {
        Messages.Add((address, subject, body));
        return Task.CompletedTask;
    }
}
