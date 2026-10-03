using FlowOps.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Auth;

public static class Permissions
{
    public const string StudioAccess = "studio.access";
    public const string PortalAccess = "portal.access";
    public const string MembersManage = "members.manage";
}

public sealed record OrganizationPermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class OrganizationPermissionHandler(
    IHttpContextAccessor accessor, FlowOpsDbContext db)
    : AuthorizationHandler<OrganizationPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        OrganizationPermissionRequirement requirement)
    {
        var http = accessor.HttpContext;
        var session = http is null ? null : AuthEndpoints.CurrentSession(http);
        if (session?.ActiveOrganizationId is not Guid organizationId) return;
        var surface = await db.Users.Where(x => x.Id == session.UserId).Select(x => x.AccountSurface).SingleAsync();
        if (requirement.Permission == Permissions.PortalAccess && surface != "Portal") return;
        if (requirement.Permission != Permissions.PortalAccess && surface != "Studio") return;

        if (requirement.Permission == Permissions.MembersManage &&
            (!Guid.TryParse(http!.Request.RouteValues["organizationId"]?.ToString(), out var routeId)
                || routeId != organizationId)) return;

        var member = await db.OrganizationMembers
            .Where(x => x.OrganizationId == organizationId && x.UserId == session.UserId)
            .Select(x => new { x.Role, x.Organization.RequireMfaForPrivileged })
            .FirstOrDefaultAsync();
        if (member is null) return;
        if (requirement.Permission == Permissions.PortalAccess)
        {
            context.Succeed(requirement);
            return;
        }
        if (OrganizationRoles.IsPrivileged(member.Role) &&
            (!member.RequireMfaForPrivileged || session.MfaSatisfied))
            context.Succeed(requirement);
    }
}
