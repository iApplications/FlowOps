using System.Text.Json.Nodes;
using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Workflows;

// Portal approval inbox (PHASE5). Tasks are visible to members of the instance's organization whose role satisfies the
// node's assignee role; the first decision wins, and requesters never decide their own tasks.
public static class ApprovalEndpoints
{
    private const int PageSize = 25;
    private const int MaxComment = 2000;

    public static void MapApprovalEndpoints(this WebApplication app)
    {
        // The same task API on both surfaces: Portal Members decide Member-role tasks, Studio Owner/Admin (MFA policy applied)
        // decide the rest. Visibility is always the member role in the active organization (PHASE5 decision 3).
        MapGroup(app, "/api/portal/tasks", Permissions.PortalAccess);
        MapGroup(app, "/api/tasks", Permissions.StudioAccess);
    }

    private static void MapGroup(WebApplication app, string prefix, string permission)
    {
        var tasks = app.MapGroup(prefix).WithTags("Approval tasks").RequireAuthorization(permission);
        tasks.MapGet("/", List);
        tasks.MapGet("/{taskId:guid}", Get);
        tasks.MapPost("/{taskId:guid}/decision", Decide).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));
        tasks.MapPost("/{taskId:guid}/comments", Comment).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));
    }

    // Owner satisfies every role, Admin satisfies Admin and Member, Member only Member.
    public static bool Satisfies(string memberRole, string assigneeRole) => Rank(memberRole) >= Rank(assigneeRole);
    private static int Rank(string role) => role switch { OrganizationRoles.Owner => 3, OrganizationRoles.Admin => 2, OrganizationRoles.Member => 1, _ => 99 };

    private static async Task<string?> MemberRole(FlowOpsDbContext db, Guid? organizationId, string userId) =>
        await db.OrganizationMembers.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.UserId == userId).Select(x => x.Role).FirstOrDefaultAsync();

    private static async Task<IResult> List(string? status, int? page, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var role = await MemberRole(db, session.ActiveOrganizationId, session.UserId);
        if (role is null) return Results.Forbid();
        var pageNumber = page ?? 1;
        if (pageNumber is < 1 or > 100000) return Results.BadRequest(new { error = "Invalid page." });
        var allowed = new[] { OrganizationRoles.Owner, OrganizationRoles.Admin, OrganizationRoles.Member }.Where(x => Satisfies(role, x)).ToList();
        var query = db.ApprovalTasks.AsNoTracking().Where(x => x.OrganizationId == session.ActiveOrganizationId && allowed.Contains(x.AssigneeRole)
            && x.RequesterUserId != session.UserId);
        query = status == "decided"
            ? query.Where(x => x.Status != ApprovalTaskStatus.Pending && x.Status != ApprovalTaskStatus.ChangesRequested)
            : query.Where(x => x.Status == ApprovalTaskStatus.Pending);
        var total = await query.CountAsync();
        var items = await (from task in query
                           join instance in db.WorkflowInstances on task.WorkflowInstanceId equals instance.Id
                           join workflow in db.WorkflowDefinitions on instance.WorkflowDefinitionId equals workflow.Id
                           join user in db.Users on task.RequesterUserId equals user.Id into users
                           from user in users.DefaultIfEmpty()
                           orderby task.CreatedAt, task.Id
                           select new
                           {
                               task.Id, task.Status, task.AssigneeRole, task.Instructions, task.CreatedAt, task.DecidedAt,
                               Workflow = workflow.Name, Requester = user == null ? null : user.DisplayName
                           }).Skip((pageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        return Results.Ok(new { items, total, page = pageNumber, pageSize = PageSize });
    }

    // The task the caller may see: same organization and a role that satisfies the assignee role. Requesters see their own
    // tasks read-only (for status tracking) but cannot decide them.
    private static async Task<(ApprovalTask? Task, bool CanDecide)> Visible(Guid taskId, HttpContext context, FlowOpsDbContext db, bool tracked = false)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var role = await MemberRole(db, session.ActiveOrganizationId, session.UserId);
        if (role is null) return (null, false);
        var query = tracked ? db.ApprovalTasks.AsQueryable() : db.ApprovalTasks.AsNoTracking();
        var task = await query.SingleOrDefaultAsync(x => x.Id == taskId && x.OrganizationId == session.ActiveOrganizationId);
        if (task is null) return (null, false);
        var isRequester = task.RequesterUserId == session.UserId;
        var assignee = Satisfies(role, task.AssigneeRole);
        return !isRequester && !assignee ? (null, false) : (task, assignee && !isRequester);
    }

    private static async Task<IResult> Get(Guid taskId, HttpContext context, FlowOpsDbContext db)
    {
        var (task, canDecide) = await Visible(taskId, context, db);
        if (task is null) return Results.NotFound(new { error = "Task not found." });
        return Results.Ok(await Detail(db, task, canDecide));
    }

    private static async Task<object> Detail(FlowOpsDbContext db, ApprovalTask task, bool canDecide)
    {
        var instance = await db.WorkflowInstances.AsNoTracking().SingleAsync(x => x.Id == task.WorkflowInstanceId);
        var workflow = await db.WorkflowDefinitions.AsNoTracking().Where(x => x.Id == instance.WorkflowDefinitionId).Select(x => x.Name).SingleAsync();
        var version = await db.WorkflowVersions.AsNoTracking().Where(x => x.Id == task.WorkflowVersionId).Select(x => x.VersionNumber).SingleAsync();
        var variables = JsonNode.Parse(instance.ContextJson) as JsonObject;
        var events = await db.ApprovalTaskEvents.AsNoTracking().Where(x => x.TaskId == task.Id).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();
        var ids = events.Select(x => x.ActorUserId).Append(task.RequesterUserId).OfType<string>().Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        string? Name(string? id) => id is not null && names.TryGetValue(id, out var n) ? n : null;
        return new
        {
            task.Id, task.Status, task.AssigneeRole, task.Instructions, task.Revision, task.CreatedAt, task.DecidedAt,
            Workflow = workflow, WorkflowVersion = version, InstanceId = instance.Id, Requester = Name(task.RequesterUserId),
            Request = variables?["request"]?.DeepClone(), Submission = variables?["submission"]?.DeepClone(),
            CanDecide = canDecide && task.Status == ApprovalTaskStatus.Pending,
            History = events.Select(x => new { x.Id, x.Kind, Actor = Name(x.ActorUserId), x.Comment, x.CreatedAt })
        };
    }

    private static string? CleanComment(string? comment, bool required, out string? error)
    {
        error = null;
        var text = comment?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            if (required) error = "A comment is required.";
            return null;
        }
        if (text.Length > MaxComment) error = $"Comments are limited to {MaxComment} characters.";
        return text;
    }

    private static async Task<IResult> Decide(Guid taskId, DecisionRequest? body, HttpContext context, FlowOpsDbContext db, WorkflowEngine engine)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var (task, canDecide) = await Visible(taskId, context, db, tracked: true);
        if (task is null) return Results.NotFound(new { error = "Task not found." });
        if (!canDecide) return Results.Json(new { error = "You cannot decide this task.", code = "not_assignee" }, statusCode: StatusCodes.Status403Forbidden);
        var decision = body?.Decision;
        if (decision is not ("approve" or "reject" or "request_changes")) return Results.BadRequest(new { error = "Decision must be approve, reject or request_changes." });
        var comment = CleanComment(body?.Comment, decision == "request_changes", out var error);
        if (error is not null) return Results.BadRequest(new { error });
        if (body?.Revision != task.Revision || task.Status != ApprovalTaskStatus.Pending)
            return Results.Conflict(new { error = "This task changed or was already decided. Reload it.", code = "task_changed" });

        var instance = await db.WorkflowInstances.SingleAsync(x => x.Id == task.WorkflowInstanceId && x.OrganizationId == task.OrganizationId);
        var (status, kind, audit) = decision switch
        {
            "approve" => (ApprovalTaskStatus.Approved, ApprovalEventKind.Approved, "approval.approved"),
            "reject" => (ApprovalTaskStatus.Rejected, ApprovalEventKind.Rejected, "approval.rejected"),
            _ => (ApprovalTaskStatus.ChangesRequested, ApprovalEventKind.ChangesRequested, "approval.changes_requested")
        };
        task.Status = status;
        task.DecidedByUserId = session.UserId;
        task.DecidedAt = DateTime.UtcNow;
        task.Revision++;
        db.ApprovalTaskEvents.Add(new() { TaskId = task.Id, OrganizationId = task.OrganizationId, Kind = kind, ActorUserId = session.UserId, Comment = comment });
        AuditService.Append(db, audit, session.UserId, task.Id.ToString(), task.OrganizationId);
        try
        {
            // The engine saves the task update, history, audit and resumed instance together.
            if (decision == "request_changes") await db.SaveChangesAsync();
            else await engine.ResumeAsync(instance, task.NodeId, decision == "approve" ? "approved" : "rejected", session.UserId, WorkflowRunEndpoints.CorrelationId(context));
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or WorkflowRuntimeException { Code: "concurrent_change" })
        {
            return Results.Conflict(new { error = "This task changed or was already decided. Reload it.", code = "task_changed" });
        }
        db.ChangeTracker.Clear();
        var saved = await db.ApprovalTasks.AsNoTracking().SingleAsync(x => x.Id == taskId);
        return Results.Ok(await Detail(db, saved, false));
    }

    private static async Task<IResult> Comment(Guid taskId, CommentRequest? body, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var (task, _) = await Visible(taskId, context, db);
        if (task is null) return Results.NotFound(new { error = "Task not found." });
        var comment = CleanComment(body?.Comment, true, out var error);
        if (error is not null) return Results.BadRequest(new { error });
        db.ApprovalTaskEvents.Add(new() { TaskId = task.Id, OrganizationId = task.OrganizationId, Kind = ApprovalEventKind.Commented, ActorUserId = session.UserId, Comment = comment });
        AuditService.Append(db, "approval.commented", session.UserId, task.Id.ToString(), task.OrganizationId);
        await db.SaveChangesAsync();
        return Results.Ok(await Detail(db, task, false));
    }
}

public sealed record DecisionRequest(string? Decision, string? Comment, int Revision);
public sealed record CommentRequest(string? Comment);
