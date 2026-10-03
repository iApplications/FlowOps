using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Forms;

public static class FormEndpoints
{
    public static void MapFormEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/forms").WithTags("Forms").RequireAuthorization(Permissions.StudioAccess);
        group.MapGet("/", async (HttpContext context, FlowOpsDbContext db) =>
        {
            var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId;
            return Results.Ok(await db.Forms.AsNoTracking().Where(x => x.OrganizationId == organizationId)
                .OrderBy(x => x.Name).ToListAsync());
        });
        group.MapPost("/", Create);
        group.MapPut("/{formId:guid}", Update);
    }

    private static string? Validate(FormDetails request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            return "Enter a form name between 1 and 120 characters.";
        return (request.Description?.Length ?? 0) > 2000 ? "Description must be 2,000 characters or fewer." : null;
    }

    private static async Task<IResult> Create(FormDetails request, HttpContext context, FlowOpsDbContext db)
    {
        if (Validate(request) is string error) return Results.BadRequest(new { error });
        var session = AuthEndpoints.CurrentSession(context)!;
        var form = new FormDefinition { OrganizationId = session.ActiveOrganizationId!.Value,
            Name = request.Name.Trim(), Description = request.Description?.Trim() ?? "" };
        db.Forms.Add(form);
        AuditService.Append(db, "form.created", session.UserId, form.Id.ToString(), form.OrganizationId);
        await db.SaveChangesAsync();
        return Results.Created($"/api/forms/{form.Id}", form);
    }

    private static async Task<IResult> Update(Guid formId, FormDetails request, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var form = await db.Forms.SingleOrDefaultAsync(x => x.Id == formId && x.OrganizationId == session.ActiveOrganizationId);
        if (form is null) return Results.NotFound(new { error = "Form not found in the active organization." });
        if (Validate(request) is string error) return Results.BadRequest(new { error });
        form.Name = request.Name.Trim();
        form.Description = request.Description?.Trim() ?? "";
        form.UpdatedAt = DateTimeOffset.UtcNow;
        AuditService.Append(db, "form.updated", session.UserId, form.Id.ToString(), form.OrganizationId);
        await db.SaveChangesAsync();
        return Results.Ok(form);
    }
}

public sealed record FormDetails(string Name, string? Description);
