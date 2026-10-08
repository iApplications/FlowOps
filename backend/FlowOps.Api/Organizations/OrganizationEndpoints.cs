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
            .Select(x => new { x.UserId, x.User.Email, x.User.DisplayName, x.Role, x.JoinedAt })
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
        if (request.Role is not (OrganizationRoles.Member or OrganizationRoles.Admin)
            || (request.Role == OrganizationRoles.Admin && manager != OrganizationRoles.Owner))
            return Results.Forbid();
        var user = await AuthEndpoints.FindAccount(users, request.Email,
            request.Role == OrganizationRoles.Member ? "Portal" : "Studio");
        if (user is null || user.IsDisabled || !user.EmailConfirmed)
            return Results.NotFound(new { error = $"No confirmed {(request.Role == OrganizationRoles.Member ? "Portal" : "Studio")} account was found for this email. Ask the user to register on that site and confirm their email first." });
        if (await db.OrganizationMembers.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == user.Id))
            return Results.Conflict(new { error = "This user is already a member." });
        db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId, UserId = user.Id, Role = request.Role
        });
        AuditService.Append(db, "organization.member_added",
            AuthEndpoints.CurrentSession(context)!.UserId, user.Id, organizationId);
        await db.SaveChangesAsync();
        return Results.Created($"/api/organizations/{organizationId}/members/{user.Id}",
            new { user.Id, user.Email, Role = request.Role });
    }

    // Roles are tied to account surfaces (Admin = Studio account, Member = Portal account) and an account never
    // changes surface, so with today's two assignable roles this endpoint cannot move anyone between them; to make
    // a Member an Admin, add that person's Studio account. It is kept for future roles within one surface.
    private static async Task<IResult> ChangeRole(Guid organizationId, string userId,
        ChangeRoleRequest request, HttpContext context, FlowOpsDbContext db)
    {
        var manager = await ManagerRole(db, context, organizationId);
        if (manager != OrganizationRoles.Owner) return Results.Forbid();
        if (request.Role is not (OrganizationRoles.Member or OrganizationRoles.Admin))
            return Results.BadRequest(new { error = "Role must be Admin or Member." });
        var member = await db.OrganizationMembers.FirstOrDefaultAsync(x => x.OrganizationId == organizationId
            && x.UserId == userId);
        if (member is null) return Results.NotFound();
        if (member.Role == OrganizationRoles.Owner) return Results.Conflict(new { error = "Owner role cannot be changed." });
        var accountSurface = await db.Users.Where(x => x.Id == userId).Select(x => x.AccountSurface).SingleAsync();
        if ((request.Role == OrganizationRoles.Admin) != (accountSurface == "Studio"))
            return Results.BadRequest(new { error = "Admin roles require a Studio account; Member roles require a Portal account." });
        member.Role = request.Role;
        AuditService.Append(db, "organization.member_role_changed",
            AuthEndpoints.CurrentSession(context)!.UserId, userId, organizationId);
        await db.SaveChangesAsync();
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
