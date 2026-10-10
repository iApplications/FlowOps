using System.Text.Json.Nodes;
using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Workflows;

// Requester status tracking in Portal (PHASE5): a person sees only the runs they started in the active organization,
// with the current step, outcome and approval history. Nothing here can change a run.
public static class MyRequestEndpoints
{
    private const int PageSize = 25;

    public static void MapMyRequestEndpoints(this WebApplication app)
    {
        var requests = app.MapGroup("/api/portal/my-requests").WithTags("My requests").RequireAuthorization(Permissions.PortalAccess);
        requests.MapGet("/", List);
        requests.MapGet("/{instanceId:guid}", Get);
    }

    private static IQueryable<WorkflowInstance> Own(FlowOpsDbContext db, HttpContext context)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        return db.WorkflowInstances.AsNoTracking().Where(x => x.OrganizationId == session.ActiveOrganizationId && x.StartedByUserId == session.UserId);
    }

    // A plain-language status for the requester, derived from the run and its open task.
    private static string Headline(WorkflowInstance instance, ApprovalTask? open) => instance.Status switch
    {
        WorkflowInstanceStatus.Waiting when open?.Status == ApprovalTaskStatus.ChangesRequested => "Changes requested",
        WorkflowInstanceStatus.Waiting when open is not null => $"Waiting for {open.AssigneeRole} approval",
        WorkflowInstanceStatus.Waiting => "Waiting",
        WorkflowInstanceStatus.Completed => string.IsNullOrWhiteSpace(instance.Outcome) ? "Completed" : instance.Outcome!,
        WorkflowInstanceStatus.Failed => "Stopped with an error",
        _ => instance.Status
    };

    private static async Task<IResult> List(int? page, HttpContext context, FlowOpsDbContext db)
    {
        var pageNumber = page ?? 1;
        if (pageNumber is < 1 or > 100000) return Results.BadRequest(new { error = "Invalid page." });
        var own = Own(db, context);
        var total = await own.CountAsync();
        var instances = await own.OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id).Skip((pageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        var ids = instances.Select(x => x.Id).ToList();
        var workflowIds = instances.Select(x => x.WorkflowDefinitionId).Distinct().ToList();
        var versionIds = instances.Select(x => x.WorkflowVersionId).Distinct().ToList();
        var workflows = await db.WorkflowDefinitions.AsNoTracking().Where(x => workflowIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var versions = await db.WorkflowVersions.AsNoTracking().Where(x => versionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var open = (await db.ApprovalTasks.AsNoTracking().Where(x => ids.Contains(x.WorkflowInstanceId)
                && (x.Status == ApprovalTaskStatus.Pending || x.Status == ApprovalTaskStatus.ChangesRequested)).ToListAsync())
            .ToDictionary(x => x.WorkflowInstanceId);
        var items = instances.Select(instance =>
        {
            open.TryGetValue(instance.Id, out var task);
            var version = versions[instance.WorkflowVersionId];
            return new
            {
                instance.Id, Workflow = workflows[instance.WorkflowDefinitionId], VersionNumber = version.VersionNumber, instance.Status, instance.Outcome,
                Headline = Headline(instance, task), CurrentStep = StepLabel(version, instance.WaitingNodeId), OpenTaskId = task?.Id, instance.StartedAt, instance.CompletedAt
            };
        });
        return Results.Ok(new { items, total, page = pageNumber, pageSize = PageSize });
    }

    private static string? StepLabel(WorkflowVersion version, string? nodeId) =>
        nodeId is null ? null : version.Graph.Nodes.FirstOrDefault(x => x.Id == nodeId)?.Label;

    private static async Task<IResult> Get(Guid instanceId, HttpContext context, FlowOpsDbContext db)
    {
        var instance = await Own(db, context).SingleOrDefaultAsync(x => x.Id == instanceId);
        if (instance is null) return Results.NotFound(new { error = "Request not found." });
        var workflow = await db.WorkflowDefinitions.AsNoTracking().Where(x => x.Id == instance.WorkflowDefinitionId).Select(x => x.Name).SingleAsync();
        var version = await db.WorkflowVersions.AsNoTracking().SingleAsync(x => x.Id == instance.WorkflowVersionId);
        var tasks = await db.ApprovalTasks.AsNoTracking().Where(x => x.WorkflowInstanceId == instance.Id).OrderBy(x => x.CreatedAt).ToListAsync();
        var taskIds = tasks.Select(x => x.Id).ToList();
        var events = (await db.ApprovalTaskEvents.AsNoTracking().Where(x => taskIds.Contains(x.TaskId)).ToListAsync())
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToLookup(x => x.TaskId);
        var userIds = events.SelectMany(x => x).Select(x => x.ActorUserId).OfType<string>().Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        string? Name(string? id) => id is not null && names.TryGetValue(id, out var n) ? n : null;
        var open = tasks.FirstOrDefault(x => x.Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.ChangesRequested);
        var variables = JsonNode.Parse(instance.ContextJson) as JsonObject;
        return Results.Ok(new
        {
            instance.Id, Workflow = workflow, VersionNumber = version.VersionNumber, instance.Status, instance.Outcome, Headline = Headline(instance, open),
            CurrentStep = StepLabel(version, instance.WaitingNodeId), OpenTaskId = open?.Id, instance.StartedAt, instance.CompletedAt,
            Request = variables?["request"]?.DeepClone(),
            Approvals = tasks.Select(task => new
            {
                task.Id, task.Status, task.AssigneeRole, task.CreatedAt, task.DecidedAt,
                History = events[task.Id].Select(e => new { e.Id, e.Kind, Actor = Name(e.ActorUserId), e.Comment, e.CreatedAt })
            })
        });
    }
}
