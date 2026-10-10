using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Workflows;

// Manual runs and run history. Studio Owner/Admin of the active organization only (PHASE4 decision 5).
public static partial class WorkflowRunEndpoints
{
    public const string CorrelationHeader = "X-Correlation-ID";
    public const string IdempotencyHeader = "Idempotency-Key";
    private const int PageSize = 25;

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex CorrelationPattern();
    [GeneratedRegex("^[\\x21-\\x7E]{1,100}$")]
    private static partial Regex IdempotencyPattern();

    public static void MapWorkflowRunEndpoints(this WebApplication app)
    {
        var workflows = app.MapGroup("/api/workflows").WithTags("Workflow runs").RequireAuthorization(Permissions.StudioAccess);
        workflows.MapPost("/{workflowId:guid}/runs", Start).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(64 * 1024));
        workflows.MapGet("/{workflowId:guid}/runs", List);
        app.MapGet("/api/workflow-instances/{instanceId:guid}", Get).WithTags("Workflow runs").RequireAuthorization(Permissions.StudioAccess);
    }

    // The caller's correlation ID when it is well formed, otherwise a new one; echoed on the response.
    public static string CorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers[CorrelationHeader].ToString();
        var id = CorrelationPattern().IsMatch(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.Response.Headers[CorrelationHeader] = id;
        return id;
    }

    // Starts a run of the latest Published version. Values in "request" become the run's request.* variables.
    private static async Task<IResult> Start(Guid workflowId, StartRunRequest? body, HttpContext context, FlowOpsDbContext db, WorkflowEngine engine)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var workflow = await db.WorkflowDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == workflowId && x.OrganizationId == session.ActiveOrganizationId);
        if (workflow is null) return Results.NotFound(new { error = "Workflow not found in the active organization." });
        var key = context.Request.Headers[IdempotencyHeader].ToString();
        if (key.Length > 0 && !IdempotencyPattern().IsMatch(key)) return Results.BadRequest(new { error = "Idempotency-Key must be 1–100 visible ASCII characters." });
        var request = body?.Request ?? [];
        var fingerprint = key.Length == 0 ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.ToJsonString())));
        if (key.Length > 0 && await Existing(db, workflow.Id, session.UserId, key) is { } existing) return await Retry(existing, fingerprint!, db);

        var version = await db.WorkflowVersions.AsNoTracking().Where(x => x.WorkflowDefinitionId == workflow.Id && x.OrganizationId == workflow.OrganizationId
            && x.Status == WorkflowVersionStatus.Published).OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync();
        if (version is null) return Results.Conflict(new { error = "Publish this workflow before running it.", code = "not_published" });
        if (version.Graph.Nodes.SingleOrDefault(x => x.Type == WorkflowNodeTypes.Start)?.Config?.Trigger != WorkflowTriggers.Manual)
            return Results.Conflict(new { error = $"Version {version.VersionNumber} starts when its form is submitted, so it cannot be run manually.", code = "trigger_not_manual" });

        WorkflowInstance instance;
        try
        {
            instance = await engine.StartAsync(new(workflow, version, WorkflowTriggers.Manual, session.UserId, request, CorrelationId(context),
                IdempotencyKey: key.Length > 0 ? key : null, RequestFingerprint: fingerprint));
        }
        catch (DbUpdateException) when (key.Length > 0)
        {
            // A concurrent request with the same key won; answer as its retry.
            db.ChangeTracker.Clear();
            return await Existing(db, workflow.Id, session.UserId, key) is { } winner ? await Retry(winner, fingerprint!, db) : throw new InvalidOperationException("Idempotent run missing.");
        }
        return Results.Created($"/api/workflow-instances/{instance.Id}", await Detail(db, instance));
    }

    private static Task<WorkflowInstance?> Existing(FlowOpsDbContext db, Guid workflowId, string userId, string key) =>
        db.WorkflowInstances.AsNoTracking().SingleOrDefaultAsync(x => x.WorkflowDefinitionId == workflowId && x.StartedByUserId == userId && x.IdempotencyKey == key);

    private static async Task<IResult> Retry(WorkflowInstance existing, string fingerprint, FlowOpsDbContext db) => existing.RequestFingerprint == fingerprint
        ? Results.Ok(await Detail(db, existing))
        : Results.Conflict(new { error = "This Idempotency-Key was already used with different values.", code = "idempotency_key_reused" });

    private static async Task<IResult> List(Guid workflowId, int? page, HttpContext context, FlowOpsDbContext db)
    {
        var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId;
        if (!await db.WorkflowDefinitions.AnyAsync(x => x.Id == workflowId && x.OrganizationId == organizationId))
            return Results.NotFound(new { error = "Workflow not found in the active organization." });
        var pageNumber = Math.Max(page ?? 1, 1);
        var runs = db.WorkflowInstances.AsNoTracking().Where(x => x.WorkflowDefinitionId == workflowId && x.OrganizationId == organizationId);
        var total = await runs.CountAsync();
        var items = await (from instance in runs
                           join version in db.WorkflowVersions on instance.WorkflowVersionId equals version.Id
                           join user in db.Users on instance.StartedByUserId equals user.Id into users
                           from user in users.DefaultIfEmpty()
                           orderby instance.StartedAt descending, instance.Id
                           select new
                           {
                               instance.Id, version.VersionNumber, instance.Status, instance.Outcome, instance.Trigger, instance.WaitingNodeId,
                               StartedBy = user == null ? null : user.DisplayName, instance.StartedAt, instance.CompletedAt,
                               instance.ErrorCode, instance.ErrorMessage, instance.CorrelationId
                           }).Skip((pageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        return Results.Ok(new { items, total, page = pageNumber, pageSize = PageSize });
    }

    private static async Task<IResult> Get(Guid instanceId, HttpContext context, FlowOpsDbContext db)
    {
        var organizationId = AuthEndpoints.CurrentSession(context)!.ActiveOrganizationId;
        var instance = await db.WorkflowInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.OrganizationId == organizationId);
        return instance is null ? Results.NotFound(new { error = "Run not found in the active organization." }) : Results.Ok(await Detail(db, instance));
    }

    // The run with its pinned version's graph (for node labels), variables, and every execution with its steps.
    private static async Task<object> Detail(FlowOpsDbContext db, WorkflowInstance instance)
    {
        var workflow = await db.WorkflowDefinitions.AsNoTracking().Where(x => x.Id == instance.WorkflowDefinitionId).Select(x => new { x.Id, x.Name }).SingleAsync();
        var version = await db.WorkflowVersions.AsNoTracking().SingleAsync(x => x.Id == instance.WorkflowVersionId);
        var executions = await db.WorkflowExecutions.AsNoTracking().Where(x => x.InstanceId == instance.Id).OrderBy(x => x.Sequence).ToListAsync();
        var steps = (await db.WorkflowExecutionSteps.AsNoTracking().Where(x => x.InstanceId == instance.Id).ToListAsync())
            .ToLookup(x => x.ExecutionId);
        var tasks = await db.ApprovalTasks.AsNoTracking().Where(x => x.WorkflowInstanceId == instance.Id).OrderBy(x => x.CreatedAt).ToListAsync();
        var taskIds = tasks.Select(x => x.Id).ToList();
        var taskEvents = (await db.ApprovalTaskEvents.AsNoTracking().Where(x => taskIds.Contains(x.TaskId)).ToListAsync())
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToLookup(x => x.TaskId);
        var userIds = executions.Select(x => x.TriggeredByUserId).Append(instance.StartedByUserId).Concat(taskEvents.SelectMany(x => x).Select(x => x.ActorUserId))
            .OfType<string>().Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        string? Name(string? userId) => userId is not null && names.TryGetValue(userId, out var name) ? name : null;
        static JsonNode? Parse(string? json) => json is null ? null : JsonNode.Parse(json);
        return new
        {
            instance.Id, Workflow = workflow, Version = new { version.Id, version.VersionNumber, version.PublishedAt, version.Graph },
            instance.Status, instance.Outcome, instance.Trigger, instance.WaitingNodeId, StartedBy = Name(instance.StartedByUserId),
            instance.SourceSubmissionId, instance.CorrelationId, instance.StartedAt, instance.CompletedAt, instance.ErrorCode, instance.ErrorMessage,
            Variables = Parse(instance.ContextJson),
            Approvals = tasks.Select(task => new
            {
                task.Id, task.NodeId, task.Status, task.AssigneeRole, task.CreatedAt, task.DecidedAt,
                History = taskEvents[task.Id].Select(e => new { e.Id, e.Kind, Actor = Name(e.ActorUserId), e.Comment, e.CreatedAt })
            }),
            Executions = executions.Select(execution => new
            {
                execution.Id, execution.Sequence, execution.Reason, execution.Status, TriggeredBy = Name(execution.TriggeredByUserId),
                execution.CorrelationId, execution.StartedAt, execution.CompletedAt, execution.ErrorCode, execution.ErrorMessage,
                Steps = steps[execution.Id].OrderBy(x => x.Sequence).Select(step => new
                {
                    step.Id, step.Sequence, step.NodeId, step.NodeType, step.Status, step.Attempt, Input = Parse(step.InputJson), Output = Parse(step.OutputJson),
                    step.DurationMs, step.ErrorCode, step.ErrorMessage, step.StartedAt, step.CompletedAt
                })
            })
        };
    }
}

public sealed record StartRunRequest(JsonObject? Request);
