using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Organizations;

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/organizations").WithTags("Organizations").RequireAuthorization();
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapPost("/{organizationId:guid}/activate", Activate);
        group.MapGet("/{organizationId:guid}/members", Members).RequireAuthorization(Permissions.MembersManage);
        group.MapPost("/{organizationId:guid}/members", AddMember).RequireAuthorization(Permissions.MembersManage);
        group.MapPut("/{organizationId:guid}/members/{userId}", ChangeRole).RequireAuthorization(Permissions.MembersManage);
        group.MapDelete("/{organizationId:guid}/members/{userId}", RemoveMember).RequireAuthorization(Permissions.MembersManage);
        app.MapGet("/api/studio/access", () => Results.Ok(new { allowed = true }))
            .RequireAuthorization(Permissions.StudioAccess).WithTags("Authorization");
        app.MapGet("/api/portal/access", () => Results.Ok(new { allowed = true }))
            .RequireAuthorization(Permissions.PortalAccess).WithTags("Authorization");
    }

    private static async Task<IResult> List(HttpContext context, FlowOpsDbContext db)
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
        var rows = await db.OrganizationMembers.Where(x => x.UserId == userId)
            .OrderBy(x => x.Organization.Name)
            .Select(x => new { x.OrganizationId, x.Organization.Name, x.Role,
                x.Organization.RequireMfaForPrivileged }).ToListAsync();
        return Results.Ok(rows);
    }

    private static async Task<IResult> Create(CreateOrganizationRequest request, HttpContext context,
        FlowOpsDbContext db, UserManager<FlowUser> users)
    {
        var user = (await users.GetUserAsync(context.User))!;
        if (!await AuthEndpoints.CanCreateWorkspace(users, db, user)) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest();
        var name = request.Name.Trim();
        if (name.Length is < 2 or > 120) return Results.BadRequest(new { error = "Name must be 2–120 characters." });
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
        var org = new Organization { Name = name };
        db.Organizations.Add(org);
        db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = org.Id, UserId = userId, Role = OrganizationRoles.Owner
        });
        var session = AuthEndpoints.CurrentSession(context)!;
        session.ActiveOrganizationId = org.Id;
        AuditService.Append(db, "organization.created", userId, org.Id.ToString(), org.Id);
        await db.SaveChangesAsync();
        return Results.Created($"/api/organizations/{org.Id}", new { org.Id, org.Name,
            Role = OrganizationRoles.Owner, org.RequireMfaForPrivileged });
    }

    private static async Task<IResult> Activate(Guid organizationId, HttpContext context,
        FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var member = await db.OrganizationMembers.AnyAsync(x => x.OrganizationId == organizationId
            && x.UserId == session.UserId);
        if (!member) return Results.NotFound();
        session.ActiveOrganizationId = organizationId;
        AuditService.Append(db, "organization.activated", session.UserId, organizationId.ToString(), organizationId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Members(Guid organizationId, HttpContext context,
        FlowOpsDbContext db)
    {
        if (await ManagerRole(db, context, organizationId) is null) return Results.Forbid();
        var members = await db.OrganizationMembers.Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.User.DisplayName)
            .Select(x => new { x.UserId, x.User.Email, x.User.DisplayName, x.Role, x.RoleId,
                RoleName = db.CustomRoles.Where(r => r.Id == x.RoleId).Select(r => r.Name).FirstOrDefault() ?? x.Role, x.JoinedAt })
            .ToListAsync();
        return Results.Ok(members);
    }

    private static async Task<IResult> AddMember(Guid organizationId, AddMemberRequest request,
        HttpContext context, UserManager<FlowUser> users, FlowOpsDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Role))
            return Results.BadRequest(new { error = "Enter an email address and select a role." });
        var email = request.Email.Trim();
        if (email.Length > 256 || !System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email)
            return Results.BadRequest(new { error = "Enter a valid email address." });
        var manager = await ManagerRole(db, context, organizationId);
        if (manager is null) return Results.Forbid();
        var assignment = await ResolveRole(db, organizationId, request.Role);
        if (assignment is null) return Results.BadRequest(new { error = "Select an active role from this organization." });
        if (assignment.BaseRole == OrganizationRoles.Owner
            || (assignment.BaseRole == OrganizationRoles.Admin && manager != OrganizationRoles.Owner))
            return Results.Forbid();
        var surface = assignment.BaseRole == OrganizationRoles.Member ? "Portal" : "Studio";
        var user = await AuthEndpoints.FindAccount(users, request.Email, surface);
        if (user is null || user.IsDisabled || !user.EmailConfirmed)
            return Results.NotFound(new { error = $"No confirmed {surface} account was found for this email. Ask the user to register on that site and confirm their email first." });
        if (await db.OrganizationMembers.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == user.Id))
            return Results.Conflict(new { error = "This user is already a member." });
        db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId, UserId = user.Id, Role = assignment.BaseRole, RoleId = assignment.RoleId
        });
        AuditService.Append(db, "organization.member_added",
            AuthEndpoints.CurrentSession(context)!.UserId, user.Id, organizationId);
        await db.SaveChangesAsync();
        return Results.Created($"/api/organizations/{organizationId}/members/{user.Id}",
            new { user.Id, user.Email, Role = assignment.BaseRole, RoleName = assignment.Name });
    }

    // Roles are tied to account surfaces (Admin-level = Studio account, Member-level = Portal account) and an account never
    // changes surface. A role on the same level is a plain change; a role on the other level moves the membership to the
    // same email's confirmed account on the other site (PHASE6), so the Owner can pick any role from one list.
    private static async Task<IResult> ChangeRole(Guid organizationId, string userId,
        ChangeRoleRequest request, HttpContext context, FlowOpsDbContext db, UserManager<FlowUser> users,
        IConfiguration config, FlowOps.Api.Email.IEmailDelivery emailDelivery, ILoggerFactory loggerFactory)
    {
        var manager = await ManagerRole(db, context, organizationId);
        if (manager != OrganizationRoles.Owner) return Results.Forbid();
        var assignment = await ResolveRole(db, organizationId, request.Role);
        if (assignment is null || assignment.BaseRole == OrganizationRoles.Owner)
            return Results.BadRequest(new { error = "Select an active role from this organization." });
        var member = await db.OrganizationMembers.FirstOrDefaultAsync(x => x.OrganizationId == organizationId
            && x.UserId == userId);
        if (member is null) return Results.NotFound();
        if (member.Role == OrganizationRoles.Owner) return Results.Conflict(new { error = "Owner role cannot be changed." });
        var account = await db.Users.Where(x => x.Id == userId).Select(x => new { x.Email, x.DisplayName, x.AccountSurface }).SingleAsync();
        var targetSurface = assignment.BaseRole == OrganizationRoles.Admin ? "Studio" : "Portal";
        var actor = AuthEndpoints.CurrentSession(context)!.UserId;
        if (account.AccountSurface == targetSurface)
        {
            member.Role = assignment.BaseRole;
            member.RoleId = assignment.RoleId;
            AuditService.Append(db, "organization.member_role_changed", actor, userId, organizationId);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }
        // Crossing levels: the person continues with their account on the other site. If they have none, it is created from
        // this one (same verified email, no password yet) and they receive a link to set a password.
        if (account.Email is null) return Results.BadRequest(new { error = "This account has no email address.", code = "account_missing" });
        var other = await AuthEndpoints.FindAccount(users, account.Email, targetSurface);
        var provisioned = false;
        if (other is null)
        {
            other = await AuthEndpoints.ProvisionAccount(users, account.Email, account.DisplayName, targetSurface);
            if (other is null) return Results.BadRequest(new { error = $"A {targetSurface} account could not be created for {account.Email}.", code = "account_missing" });
            provisioned = true;
        }
        else if (other.IsDisabled || !other.EmailConfirmed)
            return Results.BadRequest(new { error = $"The {targetSurface} account for {account.Email} is disabled or not confirmed.", code = "account_unavailable" });
        if (await db.OrganizationMembers.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == other.Id))
            return Results.Conflict(new { error = $"The {targetSurface} account for {account.Email} is already a member. Remove one of the two first.", code = "account_already_member" });
        db.OrganizationMembers.Remove(member);
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = organizationId, UserId = other.Id, Role = assignment.BaseRole, RoleId = assignment.RoleId, JoinedAt = member.JoinedAt });
        var sessions = await db.BrowserSessions.Where(x => x.UserId == userId && x.ActiveOrganizationId == organizationId).ToListAsync();
        foreach (var session in sessions) { session.ActiveOrganizationId = null; session.RevokedAt ??= DateTimeOffset.UtcNow; }
        AuditService.Append(db, "organization.member_role_changed", actor, other.Id, organizationId);
        if (provisioned) AuditService.Append(db, "account.provisioned", actor, other.Id, organizationId);
        await db.SaveChangesAsync();
        if (provisioned)
        {
            var organizationName = await db.Organizations.Where(x => x.Id == organizationId).Select(x => x.Name).SingleAsync();
            await AuthEndpoints.SendPasswordSetupEmail(users, config, emailDelivery, loggerFactory, other, organizationName);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveMember(Guid organizationId, string userId,
        HttpContext context, FlowOpsDbContext db)
    {
        var manager = await ManagerRole(db, context, organizationId);
        if (manager is null) return Results.Forbid();
        var member = await db.OrganizationMembers.FirstOrDefaultAsync(x => x.OrganizationId == organizationId
            && x.UserId == userId);
        if (member is null) return Results.NotFound();
        if (member.Role == OrganizationRoles.Owner ||
            (member.Role == OrganizationRoles.Admin && manager != OrganizationRoles.Owner))
            return Results.Forbid();
        db.OrganizationMembers.Remove(member);
        var sessions = await db.BrowserSessions.Where(x => x.UserId == userId
            && x.ActiveOrganizationId == organizationId).ToListAsync();
        foreach (var session in sessions) session.ActiveOrganizationId = null;
        AuditService.Append(db, "organization.member_removed",
            AuthEndpoints.CurrentSession(context)!.UserId, userId, organizationId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private sealed record RoleAssignment(string BaseRole, Guid? RoleId, string Name);

    // A built-in role name (Admin or Member) or an active custom role of the organization, by name.
    private static async Task<RoleAssignment?> ResolveRole(FlowOpsDbContext db, Guid organizationId, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        if (name is OrganizationRoles.Admin or OrganizationRoles.Member) return new(name, null, name);
        if (name == OrganizationRoles.Owner) return new(OrganizationRoles.Owner, null, name);
        var normalized = RoleEndpoints.Normalize(name);
        var role = await db.CustomRoles.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.NormalizedName == normalized && x.IsActive);
        return role is null ? null : new(role.BaseRole, role.Id, role.Name);
    }

    // The MembersManage policy (OrganizationPermissionHandler) has already verified that the route organization is
    // the active one, the caller is Owner/Admin there, and MFA policy is satisfied. This only reads the role for
    // the Owner-versus-Admin rules; null means the membership disappeared since authorization.
    private static Task<string?> ManagerRole(FlowOpsDbContext db, HttpContext context, Guid organizationId)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        return db.OrganizationMembers.Where(x => x.OrganizationId == organizationId && x.UserId == session.UserId
                && (x.Role == OrganizationRoles.Owner || x.Role == OrganizationRoles.Admin))
            .Select(x => (string?)x.Role).FirstOrDefaultAsync();
    }
}

public sealed record CreateOrganizationRequest(string Name);
public sealed record AddMemberRequest(string Email, string Role);
public sealed record ChangeRoleRequest(string Role);
