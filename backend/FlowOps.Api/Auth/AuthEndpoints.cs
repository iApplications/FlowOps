using System.Security.Claims;
using System.Net.Mail;
using FlowOps.Api.Data;
using FlowOps.Api.Email;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Auth;

public static class AuthEndpoints
{
    public const string SessionClaim = "flowops:session";
    public const string SessionItem = "FlowOpsSession";
    public const string StudioSignupClaim = "flowops:studio-signup";

    public static string NormalizeSurface(string? surface) => string.Equals(surface, "Studio", StringComparison.OrdinalIgnoreCase) ? "Studio" : "Portal";

    public static Task<FlowUser?> FindAccount(UserManager<FlowUser> users, string email, string? surface)
    {
        var normalizedEmail = users.NormalizeEmail(email.Trim());
        var accountSurface = NormalizeSurface(surface);
        return users.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail && x.AccountSurface == accountSurface);
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var token = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = token.RequestToken });
        });
        group.MapPost("/register", Register).RequireRateLimiting("auth");
        group.MapPost("/email/confirm", ConfirmEmail).RequireRateLimiting("auth");
        group.MapPost("/email/resend", ResendConfirmation).RequireRateLimiting("auth");
        group.MapPost("/login", Login).RequireRateLimiting("auth");
        group.MapPost("/password/reset-request", RequestPasswordReset).RequireRateLimiting("auth");
        group.MapPost("/password/reset", ResetPassword).RequireRateLimiting("auth");

        var protectedGroup = group.MapGroup("").RequireAuthorization();
        protectedGroup.MapGet("/me", Me);
        protectedGroup.MapGet("/studio-onboarding", StudioOnboarding);
        protectedGroup.MapPut("/me", UpdateProfile);
        protectedGroup.MapPost("/logout", Logout);
        protectedGroup.MapPost("/password/change", ChangePassword);
        protectedGroup.MapPost("/sessions/revoke-all", RevokeAllSessions);
        protectedGroup.MapGet("/mfa/setup", MfaSetup);
        protectedGroup.MapPost("/mfa/enable", EnableMfa);
        protectedGroup.MapPost("/mfa/disable", DisableMfa);
        protectedGroup.MapPost("/mfa/recovery-codes", RegenerateRecoveryCodes);
    }

    private static async Task<IResult> Register(RegisterRequest request, UserManager<FlowUser> users,
        FlowOpsDbContext db, IConfiguration config, IEmailDelivery emailDelivery,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName)
            || string.IsNullOrWhiteSpace(request.Password)) return Results.BadRequest();
        var email = request.Email.Trim();
        var name = request.DisplayName.Trim();
        if (email.Length > 256 || name.Length is < 1 or > 120 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
            return Results.BadRequest(new { error = "Enter a valid email and display name." });

        var surface = NormalizeSurface(request.Surface);
        if (await FindAccount(users, email, surface) is not null)
            return Results.BadRequest(new { error = "An account with this email already exists on this site." });
        var username = surface + "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(users.NormalizeEmail(email))));
        var user = new FlowUser { UserName = username, Email = email, DisplayName = name, AccountSurface = surface };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return IdentityErrors(result);

        if (string.Equals(request.Surface, "Studio", StringComparison.OrdinalIgnoreCase))
        {
            var claimResult = await users.AddClaimAsync(user, new Claim(StudioSignupClaim, "true"));
            if (!claimResult.Succeeded) return IdentityErrors(claimResult);
        }

        AuditService.Append(db, "account.register", user.Id, user.Id);
        await db.SaveChangesAsync();
        await SendConfirmation(user, users, config, emailDelivery, loggerFactory, request.Surface);
        return Results.Created("/api/auth/login", new { message = "Account created. Check your email to confirm it." });
    }

    private static async Task<IResult> ConfirmEmail(ConfirmEmailRequest request,
        UserManager<FlowUser> users, FlowOpsDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token))
            return Results.BadRequest();
        var user = await FindAccount(users, request.Email, request.Surface);
        if (user is null || user.IsDisabled) return Results.BadRequest(new { error = "Invalid confirmation link." });
        if (user.EmailConfirmed) return Results.NoContent();
        var result = await users.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
            {
                await db.Entry(user).ReloadAsync();
                if (user.EmailConfirmed && !user.IsDisabled) return Results.NoContent();
            }
            return Results.BadRequest(new { error = "Invalid or expired confirmation link. Request a new link using Resend confirmation." });
        }
        AuditService.Append(db, "account.email_confirmed", user.Id, user.Id);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ResendConfirmation(ResetRequest request,
        UserManager<FlowUser> users, IConfiguration config, IEmailDelivery emailDelivery,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return Results.BadRequest();
        var user = await FindAccount(users, request.Email, request.Surface);
        if (user is not null && !user.IsDisabled && !user.EmailConfirmed)
            await SendConfirmation(user, users, config, emailDelivery, loggerFactory, request.Surface);
        return Results.Accepted(value: new { message = "If the account needs confirmation, an email has been sent." });
    }

    private static async Task SendConfirmation(FlowUser user, UserManager<FlowUser> users,
        IConfiguration config, IEmailDelivery emailDelivery, ILoggerFactory loggerFactory, string? surface)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var appUrl = GetSurfaceUrl(config, surface);
        var link = $"{appUrl}/confirm-email?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        try
        {
            await emailDelivery.SendAsync(user.Email!, "Confirm your FlowOps account",
                $"Open this link to confirm your email address:\n{link}");
        }
        catch (SmtpException)
        {
            loggerFactory.CreateLogger("FlowOps.Mail").LogWarning("Confirmation email delivery failed");
        }
    }

    private static async Task<IResult> Login(LoginRequest request, UserManager<FlowUser> users,
        SignInManager<FlowUser> signIn, FlowOpsDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return Results.BadRequest();
        var user = await FindAccount(users, request.Email, request.Surface);
        if (user is null || user.IsDisabled)
            return Results.Json(new { error = "Email or password is incorrect." }, statusCode: 401);

        var passwordResult = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (passwordResult.IsLockedOut) return Results.Problem(statusCode: 423, title: "Account temporarily locked");
        if (!passwordResult.Succeeded)
        {
            if (passwordResult.IsNotAllowed && !user.EmailConfirmed
                && await users.CheckPasswordAsync(user, request.Password))
                return Results.Problem(statusCode: 403, title: "Confirm your email before signing in. Use Resend confirmation if needed.");
            AuditService.Append(db, "auth.login_failed", user.Id, user.Id);
            await db.SaveChangesAsync();
            return Results.Json(new { error = "Email or password is incorrect." }, statusCode: 401);
        }

        var memberships = await db.OrganizationMembers.Where(x => x.UserId == user.Id)
            .Select(x => new { x.OrganizationId, x.Role, x.JoinedAt,
                x.Organization.RequireMfaForPrivileged }).ToListAsync();
        var studioLogin = string.Equals(request.Surface, "Studio", StringComparison.OrdinalIgnoreCase);
        var firstMembership = (studioLogin
            ? memberships.Where(x => OrganizationRoles.IsPrivileged(x.Role))
            : memberships.AsEnumerable()).OrderBy(x => x.JoinedAt).FirstOrDefault();
        if (studioLogin && firstMembership is null &&
            !(await users.GetClaimsAsync(user)).Any(claim => claim.Type == StudioSignupClaim && claim.Value == "true"))
            return Results.Problem(statusCode: 403, title: "Studio access requires an Owner or Admin role");

        var mfaSatisfied = false;
        if (await users.GetTwoFactorEnabledAsync(user))
        {
            if (string.IsNullOrWhiteSpace(request.Code))
                return Results.Json(new { requiresTwoFactor = true }, statusCode: 202);

            var code = request.Code.Replace(" ", "").Replace("-", "");
            mfaSatisfied = await users.VerifyTwoFactorTokenAsync(user,
                users.Options.Tokens.AuthenticatorTokenProvider, code);
            if (!mfaSatisfied)
            {
                var recovery = await users.RedeemTwoFactorRecoveryCodeAsync(user, request.Code.Trim());
                mfaSatisfied = recovery.Succeeded;
            }
            if (!mfaSatisfied)
            {
                await users.AccessFailedAsync(user);
                AuditService.Append(db, "auth.mfa_failed", user.Id, user.Id);
                await db.SaveChangesAsync();
                if (await users.IsLockedOutAsync(user))
                    return Results.Problem(statusCode: 423, title: "Account temporarily locked");
                return Results.Json(new { error = "Authenticator or recovery code is incorrect. Use the current code for your FlowOps account." }, statusCode: 401);
            }
        }

        await users.ResetAccessFailedCountAsync(user);
        var firstOrganization = firstMembership?.OrganizationId;
        var session = new BrowserSession
        {
            UserId = user.Id,
            ActiveOrganizationId = firstOrganization,
            MfaSatisfied = mfaSatisfied
        };
        db.BrowserSessions.Add(session);
        AuditService.Append(db, "auth.login", user.Id, session.Id.ToString(), firstOrganization);
        await db.SaveChangesAsync();
        await signIn.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(SessionClaim, session.Id.ToString())]);
        return Results.Ok(new { user.Id, user.Email, user.DisplayName, session.ActiveOrganizationId });
    }

    private static async Task<IResult> Me(HttpContext context, UserManager<FlowUser> users,
        FlowOpsDbContext db)
    {
        var user = await users.GetUserAsync(context.User);
        var session = CurrentSession(context);
        if (user is null || session is null) return Results.Unauthorized();
        var active = session.ActiveOrganizationId is Guid id
            ? await db.OrganizationMembers.Where(x => x.UserId == user.Id && x.OrganizationId == id)
                .Select(x => new { x.OrganizationId, x.Organization.Name, x.Role,
                    x.Organization.RequireMfaForPrivileged }).FirstOrDefaultAsync()
            : null;
        var canCreateWorkspace = await CanCreateWorkspace(users, db, user);
        return Results.Ok(new { user.Id, user.Email, user.DisplayName, user.TwoFactorEnabled, user.AccountSurface, CanCreateWorkspace = canCreateWorkspace,
            session.ExpiresAt, session.MfaSatisfied, ActiveOrganization = active });
    }

    public static async Task<bool> CanCreateWorkspace(UserManager<FlowUser> users, FlowOpsDbContext db, FlowUser user)
    {
        if (user.AccountSurface != "Studio") return false;
        if ((await users.GetClaimsAsync(user)).Any(claim => claim.Type == StudioSignupClaim && claim.Value == "true"))
            return true;
        return await db.OrganizationMembers.AnyAsync(member => member.UserId == user.Id &&
            (member.Role == OrganizationRoles.Owner || member.Role == OrganizationRoles.Admin));
    }

    private static async Task<IResult> StudioOnboarding(HttpContext context, UserManager<FlowUser> users,
        FlowOpsDbContext db)
    {
        var user = (await users.GetUserAsync(context.User))!;
        return await CanCreateWorkspace(users, db, user)
            ? Results.Ok(new { allowed = true })
            : Results.Forbid();
    }

    private static async Task<IResult> UpdateProfile(ProfileRequest request, HttpContext context,
        UserManager<FlowUser> users, FlowOpsDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest();
        var name = request.DisplayName.Trim();
        if (name.Length is < 1 or > 120) return Results.BadRequest(new { error = "Display name must be 1–120 characters." });
        var user = (await users.GetUserAsync(context.User))!;
        user.DisplayName = name;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return IdentityErrors(result);
        AuditService.Append(db, "account.profile_updated", user.Id, user.Id);
        await db.SaveChangesAsync();
        return Results.Ok(new { user.Id, user.Email, user.DisplayName });
    }

    private static async Task<IResult> Logout(HttpContext context, SignInManager<FlowUser> signIn,
        FlowOpsDbContext db)
    {
        var session = CurrentSession(context)!;
        session.RevokedAt = DateTimeOffset.UtcNow;
        AuditService.Append(db, "auth.logout", session.UserId, session.Id.ToString(), session.ActiveOrganizationId);
        await db.SaveChangesAsync();
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(PasswordChangeRequest request, HttpContext context,
        UserManager<FlowUser> users, SignInManager<FlowUser> signIn, FlowOpsDbContext db)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
            return Results.BadRequest();
        var user = (await users.GetUserAsync(context.User))!;
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return IdentityErrors(result);
        await RevokeSessions(db, user.Id, "auth.password_changed");
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RequestPasswordReset(ResetRequest request,
        UserManager<FlowUser> users, IConfiguration config, FlowOpsDbContext db,
        IEmailDelivery emailDelivery, ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return Results.BadRequest();
        var user = await FindAccount(users, request.Email, request.Surface);
        if (user is not null && !user.IsDisabled && user.EmailConfirmed)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var appUrl = GetSurfaceUrl(config, request.Surface);
            var link = $"{appUrl}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
            try
            {
                await emailDelivery.SendAsync(user.Email!, "Reset your FlowOps password",
                    $"Open this link to reset your password:\n{link}\n\nIf you did not request this, ignore this email.");
                AuditService.Append(db, "auth.password_reset_requested", user.Id, user.Id);
                await db.SaveChangesAsync();
            }
            catch (SmtpException)
            {
                loggerFactory.CreateLogger("FlowOps.Mail").LogWarning("Password reset email delivery failed");
            }
        }
        return Results.Accepted(value: new { message = "If the account exists, a reset email has been sent." });
    }

    private static string GetSurfaceUrl(IConfiguration config, string? surface) =>
        (string.Equals(surface, "Studio", StringComparison.OrdinalIgnoreCase)
            ? config["StudioUrl"] ?? "http://localhost:3000"
            : config["PortalUrl"] ?? "http://localhost:3001").TrimEnd('/');

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request,
        UserManager<FlowUser> users, FlowOpsDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrEmpty(request.NewPassword)) return Results.BadRequest();
        var user = await FindAccount(users, request.Email, request.Surface);
        if (user is null || user.IsDisabled) return Results.BadRequest(new { error = "Invalid reset request." });
        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "InvalidToken"))
                return Results.BadRequest(new { error = "This reset link is invalid, expired, or already used. Request a new link from Forgot password." });
            return IdentityErrors(result);
        }
        await RevokeSessions(db, user.Id, "auth.password_reset");
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAllSessions(HttpContext context,
        UserManager<FlowUser> users, SignInManager<FlowUser> signIn, FlowOpsDbContext db)
    {
        var user = (await users.GetUserAsync(context.User))!;
        await RevokeSessions(db, user.Id, "auth.sessions_revoked");
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task RevokeSessions(FlowOpsDbContext db, string userId, string action)
    {
        var sessions = await db.BrowserSessions.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync();
        foreach (var session in sessions) session.RevokedAt = DateTimeOffset.UtcNow;
        AuditService.Append(db, action, userId, userId);
        await db.SaveChangesAsync();
    }

    private static async Task<IResult> MfaSetup(HttpContext context, UserManager<FlowUser> users,
        SignInManager<FlowUser> signIn)
    {
        var user = (await users.GetUserAsync(context.User))!;
        if (user.TwoFactorEnabled) return Results.Conflict(new { error = "MFA is already enabled." });
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (key is null)
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
            await signIn.SignInWithClaimsAsync(user, isPersistent: false,
                [new Claim(SessionClaim, CurrentSession(context)!.Id.ToString())]);
        }
        var uri = $"otpauth://totp/FlowOps:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer=FlowOps&digits=6&algorithm=SHA1&period=30";
        return Results.Ok(new { sharedKey = key, authenticatorUri = uri });
    }

    private static async Task<IResult> EnableMfa(MfaCodeRequest request, HttpContext context,
        UserManager<FlowUser> users, FlowOpsDbContext db, SignInManager<FlowUser> signIn)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) return Results.BadRequest();
        var user = (await users.GetUserAsync(context.User))!;
        if (user.TwoFactorEnabled) return Results.Conflict(new { error = "MFA is already enabled." });
        var valid = await users.VerifyTwoFactorTokenAsync(user,
            users.Options.Tokens.AuthenticatorTokenProvider, request.Code.Replace(" ", "").Replace("-", ""));
        if (!valid) return Results.BadRequest(new { error = "Invalid authenticator code. Enter the current six-digit code for FlowOps and make sure your phone uses automatic date and time." });
        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        CurrentSession(context)!.MfaSatisfied = true;
        AuditService.Append(db, "auth.mfa_enabled", user.Id, user.Id);
        await db.SaveChangesAsync();
        await signIn.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(SessionClaim, CurrentSession(context)!.Id.ToString())]);
        return Results.Ok(new { recoveryCodes = codes });
    }

    private static async Task<IResult> DisableMfa(MfaDisableRequest request, HttpContext context,
        UserManager<FlowUser> users, FlowOpsDbContext db, SignInManager<FlowUser> signIn)
    {
        if (string.IsNullOrEmpty(request.Password) || string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest();
        var user = (await users.GetUserAsync(context.User))!;
        if (!await users.CheckPasswordAsync(user, request.Password)) return Results.Unauthorized();
        if (!await users.VerifyTwoFactorTokenAsync(user,
            users.Options.Tokens.AuthenticatorTokenProvider, request.Code.Replace(" ", "").Replace("-", "")))
            return Results.Unauthorized();
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await RevokeSessions(db, user.Id, "auth.mfa_disabled");
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RegenerateRecoveryCodes(MfaCodeRequest request, HttpContext context,
        UserManager<FlowUser> users, FlowOpsDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) return Results.BadRequest();
        var user = (await users.GetUserAsync(context.User))!;
        if (!user.TwoFactorEnabled || !await users.VerifyTwoFactorTokenAsync(user,
            users.Options.Tokens.AuthenticatorTokenProvider, request.Code.Replace(" ", "").Replace("-", "")))
            return Results.Unauthorized();
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        AuditService.Append(db, "auth.recovery_codes_regenerated", user.Id, user.Id);
        await db.SaveChangesAsync();
        return Results.Ok(new { recoveryCodes = codes });
    }

    private static IResult IdentityErrors(IdentityResult result) => Results.ValidationProblem(
        result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));

    public static BrowserSession? CurrentSession(HttpContext context) => context.Items[SessionItem] as BrowserSession;
}

public sealed record RegisterRequest(string Email, string Password, string DisplayName, string? Surface = null);
public sealed record ConfirmEmailRequest(string Email, string Token, string? Surface = null);
public sealed record LoginRequest(string Email, string Password, string? Code, string? Surface = null);
public sealed record ProfileRequest(string DisplayName);
public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword);
public sealed record ResetRequest(string Email, string? Surface = null);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword, string? Surface = null);
public sealed record MfaCodeRequest(string Code);
public sealed record MfaDisableRequest(string Password, string Code);
