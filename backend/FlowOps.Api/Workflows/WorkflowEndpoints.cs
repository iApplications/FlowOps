using System.Text.Json;
using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Workflows;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/workflows").WithTags("Workflows").RequireAuthorization(Permissions.StudioAccess);
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapGet("/{workflowId:guid}", Get);
        group.MapPut("/{workflowId:guid}", Update);
        group.MapPut("/{workflowId:guid}/draft", SaveDraft).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(512 * 1024));
    }

    private static async Task<IResult> SaveDraft(Guid workflowId, SaveDraftRequest request, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var workflow = await db.WorkflowDefinitions.SingleOrDefaultAsync(x => x.Id == workflowId && x.OrganizationId == session.ActiveOrganizationId);
        if (workflow is null) return NotFound();
        var draft = await db.WorkflowVersions.SingleOrDefaultAsync(x => x.WorkflowDefinitionId == workflow.Id && x.OrganizationId == workflow.OrganizationId && x.Status == WorkflowVersionStatus.Draft);
        if (draft is null) return Results.Conflict(new { error = "This workflow has no editable draft." });
        if (request.DraftRevision is null) return Results.BadRequest(new { error = "Provide the draft revision you are editing." });
        if (request.DraftRevision != draft.DraftRevision) return Results.Conflict(new { error = "This draft changed in another session. Reload it before saving." });
        // Form triggers may only reference forms of this organization; load the referenced IDs in one query.
        var referencedForms = request.Graph?.Nodes?.Select(x => x?.Config?.FormId).OfType<Guid>().Distinct().ToList() ?? [];
        var organizationForms = (await db.Forms.Where(x => x.OrganizationId == workflow.OrganizationId && referencedForms.Contains(x.Id)).Select(x => x.Id).ToListAsync()).ToHashSet();
        if (WorkflowGraphValidation.Validate(request.Graph, organizationForms.Contains) is string error) return Results.BadRequest(new { error });
        // Store a normalized copy: trimmed labels, settings only as the typed model defines them.
        var graph = request.Graph! with { Nodes = request.Graph.Nodes.Select(x => x with { Label = x.Label.Trim(), Config = x.Config ?? new() }).ToList() };
        draft.GraphJson = JsonSerializer.Serialize(graph);
        draft.DraftRevision++;
        // Only the draft row changes: touching the definition would make graph saves conflict with renames.
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        AuditService.Append(db, "workflow.draft_saved", session.UserId, workflow.Id.ToString(), workflow.OrganizationId);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "This draft changed in another session. Reload it before saving." }); }
        return Results.Ok(await Detail(db, workflow));
    }

    private static async Task<IResult> List(HttpContext context, FlowOpsDbContext db)
    {
        var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId!.Value;
        var workflows = await db.WorkflowDefinitions.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.Revision, x.CreatedAt, x.UpdatedAt,
                PublishedVersions = db.WorkflowVersions.Count(v => v.WorkflowDefinitionId == x.Id && v.Status == WorkflowVersionStatus.Published)
            }).ToListAsync();
        var drafts = (await db.WorkflowVersions.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Status == WorkflowVersionStatus.Draft).ToListAsync())
            .ToDictionary(x => x.WorkflowDefinitionId);
        var forms = await FormStates(db, organizationId, drafts.Values.SelectMany(x => x.Graph.Nodes));
        return Results.Ok(workflows.Select(x => new
        {
            x.Id, x.Name, x.Description, x.Revision, x.CreatedAt, x.UpdatedAt, x.PublishedVersions,
            DraftVersionNumber = drafts.TryGetValue(x.Id, out var draft) ? draft.VersionNumber : (int?)null,
            IssueCount = draft is null ? (int?)null : WorkflowGraphCompleteness.Issues(draft.Graph, forms).Count
        }));
    }

    private static async Task<IResult> Get(Guid workflowId, HttpContext context, FlowOpsDbContext db)
    {
        var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId;
        var workflow = await db.WorkflowDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == workflowId && x.OrganizationId == organizationId);
        return workflow is null ? NotFound() : Results.Ok(await Detail(db, workflow));
    }

    private static async Task<IResult> Create(WorkflowDetails request, HttpContext context, FlowOpsDbContext db)
    {
        if (Validate(request) is string error) return Results.BadRequest(new { error });
        var session = AuthEndpoints.CurrentSession(context)!;
        var workflow = new WorkflowDefinition { OrganizationId = session.ActiveOrganizationId!.Value,
            Name = request.Name.Trim(), Description = request.Description?.Trim() ?? "" };
        var draft = new WorkflowVersion { WorkflowDefinitionId = workflow.Id, OrganizationId = workflow.OrganizationId,
            VersionNumber = 1, GraphJson = JsonSerializer.Serialize(WorkflowGraph.Starter()) };
        db.WorkflowDefinitions.Add(workflow);
        db.WorkflowVersions.Add(draft);
        AuditService.Append(db, "workflow.created", session.UserId, workflow.Id.ToString(), workflow.OrganizationId);
        await db.SaveChangesAsync();
        return Results.Created($"/api/workflows/{workflow.Id}", await Detail(db, workflow));
    }

    private static async Task<IResult> Update(Guid workflowId, WorkflowDetails request, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var workflow = await db.WorkflowDefinitions.SingleOrDefaultAsync(x => x.Id == workflowId && x.OrganizationId == session.ActiveOrganizationId);
        if (workflow is null) return NotFound();
        if (Validate(request) is string error) return Results.BadRequest(new { error });
        if (request.Revision is null) return Results.BadRequest(new { error = "Provide the workflow revision you are editing." });
        if (request.Revision != workflow.Revision) return Conflict();
        workflow.Name = request.Name.Trim();
        workflow.Description = request.Description?.Trim() ?? "";
        workflow.UpdatedAt = DateTimeOffset.UtcNow;
        workflow.Revision++;
        AuditService.Append(db, "workflow.updated", session.UserId, workflow.Id.ToString(), workflow.OrganizationId);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        return Results.Ok(await Detail(db, workflow));
    }

    private static async Task<object> Detail(FlowOpsDbContext db, WorkflowDefinition workflow)
    {
        var draft = await db.WorkflowVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkflowDefinitionId == workflow.Id && x.OrganizationId == workflow.OrganizationId && x.Status == WorkflowVersionStatus.Draft);
        var publishedVersions = await db.WorkflowVersions.CountAsync(x => x.WorkflowDefinitionId == workflow.Id && x.Status == WorkflowVersionStatus.Published);
        var graph = draft?.Graph;
        var issues = graph is null ? new List<WorkflowIssue>() : WorkflowGraphCompleteness.Issues(graph, await FormStates(db, workflow.OrganizationId, graph.Nodes));
        return new
        {
            workflow.Id, workflow.Name, workflow.Description, workflow.Revision, workflow.CreatedAt, workflow.UpdatedAt, PublishedVersions = publishedVersions,
            Draft = draft is null ? null : new { draft.Id, draft.VersionNumber, draft.Status, draft.DraftRevision, Graph = graph, Issues = issues, draft.UpdatedAt }
        };
    }

    // Name and published state of the organization's forms referenced by Start triggers.
    private static async Task<Dictionary<Guid, (string Name, bool Published)>> FormStates(FlowOpsDbContext db, Guid organizationId, IEnumerable<WorkflowNode> nodes)
    {
        var formIds = nodes.Select(x => x.Config?.FormId).OfType<Guid>().Distinct().ToList();
        if (formIds.Count == 0) return new();
        var forms = await db.Forms.AsNoTracking().Where(x => x.OrganizationId == organizationId && formIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.PublicationCount }).ToListAsync();
        return forms.ToDictionary(x => x.Id, x => (x.Name, x.PublicationCount > 0));
    }

    private static string? Validate(WorkflowDetails request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            return "Enter a workflow name between 1 and 120 characters.";
        return (request.Description?.Length ?? 0) > 2000 ? "Description must be 2,000 characters or fewer." : null;
    }

    private static IResult NotFound() => Results.NotFound(new { error = "Workflow not found in the active organization." });
    private static IResult Conflict() => Results.Conflict(new { error = "This workflow changed. Reload it before saving." });
}

public sealed record WorkflowDetails(string Name, string? Description, int? Revision = null);
public sealed record SaveDraftRequest(WorkflowGraph? Graph, int? DraftRevision);
