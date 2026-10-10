using System.Text.RegularExpressions;
using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Organizations;

// Custom organization roles (PHASE6 decision 9). The Owner is the only role manager. A custom role carries a base access
// level (Admin = Studio accounts, Member = Portal accounts) that every permission check keeps using, plus a business name
// that Approval nodes can target. Names never change, so workflows and tasks that reference them stay valid.
public static partial class RoleEndpoints
{
    public const int MaxCustomRoles = 20;

    public static void MapRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/roles").WithTags("Organization roles")
            .RequireAuthorization(Permissions.MembersManage);
        group.MapGet("/", (Guid organizationId, FlowOpsDbContext db) => List(organizationId, db));
        // The same list for the active organization, for Studio pickers such as the workflow designer.
        app.MapGet("/api/roles", (HttpContext context, FlowOpsDbContext db) =>
            AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId is Guid active ? List(active, db) : Task.FromResult<IResult>(Results.Ok(Array.Empty<object>())))
            .WithTags("Organization roles").RequireAuthorization(Permissions.StudioAccess);
        group.MapPost("/", Create);
        group.MapPut("/{roleId:guid}", SetActive);
        group.MapDelete("/{roleId:guid}", Delete);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _-]{0,30}[A-Za-z0-9]$")]
    private static partial Regex NamePattern();

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private static async Task<bool> IsOwner(FlowOpsDbContext db, HttpContext context, Guid organizationId)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        return await db.OrganizationMembers.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == session.UserId
            && x.Role == OrganizationRoles.Owner);
    }

    private static async Task<IResult> List(Guid organizationId, FlowOpsDbContext db)
    {
        var counts = await db.OrganizationMembers.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.RoleId != null)
            .GroupBy(x => x.RoleId!.Value).Select(x => new { RoleId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.RoleId, x => x.Count);
        var custom = await db.CustomRoles.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name).ToListAsync();
        var builtIn = new[] { OrganizationRoles.Owner, OrganizationRoles.Admin, OrganizationRoles.Member }
            .Select(x => new { Id = (Guid?)null, Name = x, BaseRole = x, IsBuiltIn = true, IsActive = true, MemberCount = (int?)null });
        var items = builtIn.Concat(custom.Select(x => new
        {
            Id = (Guid?)x.Id, x.Name, x.BaseRole, IsBuiltIn = false, x.IsActive, MemberCount = (int?)counts.GetValueOrDefault(x.Id)
        }));
        return Results.Ok(items);
    }

    private static async Task<IResult> Create(Guid organizationId, CreateRoleRequest? request, HttpContext context, FlowOpsDbContext db)
    {
        if (!await IsOwner(db, context, organizationId)) return Results.Forbid();
        var name = request?.Name?.Trim();
        if (name is null || !NamePattern().IsMatch(name))
            return Results.BadRequest(new { error = "Role names are 2–32 letters, digits, spaces, '-' or '_'." });
        if (new[] { OrganizationRoles.Owner, OrganizationRoles.Admin, OrganizationRoles.Member }.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            return Results.Conflict(new { error = "That name is reserved for a built-in role.", code = "role_name_reserved" });
        if (request!.BaseRole is not (OrganizationRoles.Admin or OrganizationRoles.Member))
            return Results.BadRequest(new { error = "Base access must be Admin (Studio) or Member (Portal)." });
        if (await db.CustomRoles.CountAsync(x => x.OrganizationId == organizationId) >= MaxCustomRoles)
            return Results.BadRequest(new { error = $"An organization can have at most {MaxCustomRoles} custom roles." });
        var normalized = Normalize(name);
        if (await db.CustomRoles.AnyAsync(x => x.OrganizationId == organizationId && x.NormalizedName == normalized))
            return Results.Conflict(new { error = "A role with that name already exists.", code = "role_exists" });
        var role = new OrganizationRole { OrganizationId = organizationId, Name = name, NormalizedName = normalized, BaseRole = request.BaseRole };
        db.CustomRoles.Add(role);
        AuditService.Append(db, "organization.role_created", AuthEndpoints.CurrentSession(context)!.UserId, role.Id.ToString(), organizationId);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "A role with that name already exists.", code = "role_exists" }); }
        return Results.Created($"/api/organizations/{organizationId}/roles/{role.Id}",
            new { role.Id, role.Name, role.BaseRole, IsBuiltIn = false, role.IsActive, MemberCount = 0 });
    }

    // Activate or deactivate. A deactivated role cannot be assigned or picked in workflows; existing assignments keep working.
    private static async Task<IResult> SetActive(Guid organizationId, Guid roleId, UpdateRoleRequest? request, HttpContext context, FlowOpsDbContext db)
    {
        if (!await IsOwner(db, context, organizationId)) return Results.Forbid();
        if (request?.IsActive is not bool active) return Results.BadRequest(new { error = "Provide isActive." });
        var role = await db.CustomRoles.SingleOrDefaultAsync(x => x.Id == roleId && x.OrganizationId == organizationId);
        if (role is null) return Results.NotFound();
        if (role.IsActive != active)
        {
            role.IsActive = active;
            AuditService.Append(db, active ? "organization.role_activated" : "organization.role_deactivated",
                AuthEndpoints.CurrentSession(context)!.UserId, role.Id.ToString(), organizationId);
            await db.SaveChangesAsync();
        }
        return Results.Ok(new { role.Id, role.Name, role.BaseRole, IsBuiltIn = false, role.IsActive });
    }

    private static async Task<IResult> Delete(Guid organizationId, Guid roleId, HttpContext context, FlowOpsDbContext db)
    {
        if (!await IsOwner(db, context, organizationId)) return Results.Forbid();
        var role = await db.CustomRoles.SingleOrDefaultAsync(x => x.Id == roleId && x.OrganizationId == organizationId);
        if (role is null) return Results.NotFound();
        var inUse = await db.OrganizationMembers.AnyAsync(x => x.RoleId == role.Id)
            || await db.ApprovalTasks.AnyAsync(x => x.OrganizationId == organizationId && x.AssigneeRole == role.Name)
            || (await db.WorkflowVersions.AsNoTracking().Where(x => x.OrganizationId == organizationId).ToListAsync())
                .Any(x => x.Graph.Nodes.Any(n => n.Config?.AssigneeRole == role.Name));
        if (inUse)
            return Results.Conflict(new { error = "This role is assigned to people or used by workflows or tasks. Deactivate it instead.", code = "role_in_use" });
        db.CustomRoles.Remove(role);
        AuditService.Append(db, "organization.role_deleted", AuthEndpoints.CurrentSession(context)!.UserId, role.Id.ToString(), organizationId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}

public sealed record CreateRoleRequest(string? Name, string? BaseRole);
public sealed record UpdateRoleRequest(bool? IsActive);
