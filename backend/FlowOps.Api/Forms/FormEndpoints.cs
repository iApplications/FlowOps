using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Forms;

public static class FormEndpoints
{
    public static void MapFormEndpoints(this WebApplication app)
    {
        app.MapSubmissionEndpoints();
        var group = app.MapGroup("/api/forms").WithTags("Forms").RequireAuthorization(Permissions.StudioAccess);
        group.MapGet("/", async (HttpContext context, FlowOpsDbContext db) =>
        {
            var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId;
            return Results.Ok(await db.Forms.AsNoTracking().Where(x => x.OrganizationId == organizationId)
                .OrderBy(x => x.Name).ToListAsync());
        });
        group.MapPost("/", Create);
        group.MapGet("/{formId:guid}", async (Guid formId, HttpContext context, FlowOpsDbContext db) =>
        {
            var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId;
            var form = await db.Forms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == formId && x.OrganizationId == organizationId);
            return form is null ? Results.NotFound(new { error = "Form not found in the active organization." }) : Results.Ok(await WithPublishState(db, form));
        });
        group.MapPut("/{formId:guid}/fields", SaveFields);
        group.MapPut("/{formId:guid}", Update);
        group.MapPost("/{formId:guid}/publish", Publish);
        var portal = app.MapGroup("/api/portal/forms").WithTags("Published forms").RequireAuthorization(Permissions.PortalAccess);
        portal.MapGet("/", async (HttpContext context, FlowOpsDbContext db) =>
        {
            var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId;
            return Results.Ok(await db.FormPublications.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && !db.FormPublications.Any(newer => newer.FormId == x.FormId && newer.VersionNumber > x.VersionNumber))
                .OrderBy(x => x.Name).Select(x => new { x.FormId, x.Id, x.VersionNumber, x.Name, x.Description, x.PublishedAt }).ToListAsync());
        });
        portal.MapGet("/{formId:guid}", async (Guid formId, HttpContext context, FlowOpsDbContext db) =>
        {
            var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId;
            var publication = await db.FormPublications.AsNoTracking().Where(x => x.FormId == formId && x.OrganizationId == organizationId)
                .OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync();
            return publication is null ? Results.NotFound(new { error = "Published form not found in the active organization." }) : Results.Ok(publication);
        });
    }

    private static async Task<IResult> Publish(Guid formId, PublishFormRequest request, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var form = await db.Forms.SingleOrDefaultAsync(x => x.Id == formId && x.OrganizationId == session.ActiveOrganizationId);
        if (form is null) return Results.NotFound(new { error = "Form not found in the active organization." });
        if (form.DraftRevision != request.DraftRevision) return Results.Conflict(new { error = "This draft changed. Reload it before publishing." });
        if (form.Fields.Count == 0) return Results.BadRequest(new { error = "Add and save at least one field before publishing." });
        if (FormFieldValidation.Validate(form.Fields) is string error) return Results.BadRequest(new { error });
        // An identical republish would add an empty version and force open Portal pages to reload for nothing.
        if (await LatestPublication(db, form) is { } latest && SameAsDraft(latest, form))
            return Results.Conflict(new { error = $"No changes since version {latest.VersionNumber}. Edit and save the draft before publishing.", code = "no_changes" });
        var publication = new FormPublication { FormId = form.Id, OrganizationId = form.OrganizationId,
            VersionNumber = ++form.PublicationCount, Name = form.Name, Description = form.Description, FieldsJson = form.FieldsJson };
        form.DraftRevision++;
        form.UpdatedAt = DateTimeOffset.UtcNow;
        db.FormPublications.Add(publication);
        AuditService.Append(db, "form.published", session.UserId, publication.Id.ToString(), form.OrganizationId);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "This draft changed. Reload it before publishing." }); }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Results.Conflict(new { error = "Another session published this draft. Reload it before publishing." }); }
        form.HasUnpublishedChanges = false;
        return Results.Ok(form);
    }

    private static Task<FormPublication?> LatestPublication(FlowOpsDbContext db, FormDefinition form) =>
        db.FormPublications.AsNoTracking().Where(x => x.FormId == form.Id && x.OrganizationId == form.OrganizationId)
            .OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync();

    // Publications copy the draft's name, description and serialized fields verbatim, so exact comparison is reliable.
    private static bool SameAsDraft(FormPublication publication, FormDefinition form) =>
        publication.Name == form.Name && publication.Description == form.Description && publication.FieldsJson == form.FieldsJson;

    private static async Task<FormDefinition> WithPublishState(FlowOpsDbContext db, FormDefinition form)
    {
        form.HasUnpublishedChanges = await LatestPublication(db, form) is not { } latest || !SameAsDraft(latest, form);
        return form;
    }

    private static string? Validate(FormDetails request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            return "Enter a form name between 1 and 120 characters.";
        return (request.Description?.Length ?? 0) > 2000 ? "Description must be 2,000 characters or fewer." : null;
    }

    private static async Task<IResult> SaveFields(Guid formId, FormFieldsRequest request, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var form = await db.Forms.SingleOrDefaultAsync(x => x.Id == formId && x.OrganizationId == session.ActiveOrganizationId);
        if (form is null) return Results.NotFound(new { error = "Form not found in the active organization." });
        if (request.DraftRevision != form.DraftRevision) return Results.Conflict(new { error = "This draft changed in another session. Reload it before saving." });
        if (FormFieldValidation.Validate(request.Fields) is string error) return Results.BadRequest(new { error });
        form.FieldsJson = System.Text.Json.JsonSerializer.Serialize(request.Fields);
        form.DraftRevision++;
        form.UpdatedAt = DateTimeOffset.UtcNow;
        AuditService.Append(db, "form.fields_updated", session.UserId, form.Id.ToString(), form.OrganizationId);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "This draft changed in another session. Reload it before saving." }); }
        return Results.Ok(await WithPublishState(db, form));
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
        if (request.DraftRevision is null) return Results.BadRequest(new { error = "Provide the draft revision you are editing." });
        if (request.DraftRevision != form.DraftRevision) return Results.Conflict(new { error = "This draft changed. Reload it before saving." });
        form.Name = request.Name.Trim();
        form.Description = request.Description?.Trim() ?? "";
        form.UpdatedAt = DateTimeOffset.UtcNow;
        form.DraftRevision++;
        AuditService.Append(db, "form.updated", session.UserId, form.Id.ToString(), form.OrganizationId);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "This draft changed. Reload it before saving." }); }
        return Results.Ok(await WithPublishState(db, form));
    }
}

public sealed record FormDetails(string Name, string? Description, int? DraftRevision = null);
public sealed record FormFieldsRequest(List<FormField>? Fields, int DraftRevision);
public sealed record PublishFormRequest(int DraftRevision);
