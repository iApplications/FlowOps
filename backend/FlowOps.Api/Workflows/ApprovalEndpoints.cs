using System.Text.Json;
using System.Text.Json.Nodes;
using FlowOps.Api.Auth;
using FlowOps.Api.Data;
using FlowOps.Api.Forms;
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
        tasks.MapPost("/{taskId:guid}/resubmit", Resubmit).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(64 * 1024));
        tasks.MapPost("/{taskId:guid}/comments", Comment).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));
    }

    // Built-in assignee roles: Owner satisfies every role, Admin satisfies Admin and Member, Member only Member, by base access role.
    // A custom assignee role (PHASE6) is satisfied by the Owner or by a member holding exactly that role.
    public static bool Satisfies(string memberRole, string assigneeRole, string? memberRoleName = null) =>
        OrganizationRoles.IsBuiltIn(assigneeRole)
            ? Rank(memberRole) >= Rank(assigneeRole)
            : memberRole == OrganizationRoles.Owner || string.Equals(memberRoleName, assigneeRole, StringComparison.Ordinal);
    private static int Rank(string role) => role switch { OrganizationRoles.Owner => 3, OrganizationRoles.Admin => 2, OrganizationRoles.Member => 1, _ => 99 };

    private sealed record MemberAccess(string Role, string? RoleName);

    private static async Task<MemberAccess?> MemberRole(FlowOpsDbContext db, Guid? organizationId, string userId) =>
        await (from member in db.OrganizationMembers.AsNoTracking()
               join role in db.CustomRoles.AsNoTracking() on member.RoleId equals role.Id into roles
               from role in roles.DefaultIfEmpty()
               where member.OrganizationId == organizationId && member.UserId == userId
               select new MemberAccess(member.Role, role == null || !role.IsActive ? null : role.Name)).FirstOrDefaultAsync();

    private static async Task<IResult> List(string? status, int? page, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var access = await MemberRole(db, session.ActiveOrganizationId, session.UserId);
        if (access is null) return Results.Forbid();
        var role = access.Role;
        var pageNumber = page ?? 1;
        if (pageNumber is < 1 or > 100000) return Results.BadRequest(new { error = "Invalid page." });
        var allowed = new[] { OrganizationRoles.Owner, OrganizationRoles.Admin, OrganizationRoles.Member }.Where(x => Satisfies(role, x)).ToList();
        if (access.RoleName is { } roleName) allowed.Add(roleName);
        var isOwner = role == OrganizationRoles.Owner;
        var query = db.ApprovalTasks.AsNoTracking().Where(x => x.OrganizationId == session.ActiveOrganizationId && (isOwner || allowed.Contains(x.AssigneeRole))
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
        var access = await MemberRole(db, session.ActiveOrganizationId, session.UserId);
        if (access is null) return (null, false);
        var query = tracked ? db.ApprovalTasks.AsQueryable() : db.ApprovalTasks.AsNoTracking();
        var task = await query.SingleOrDefaultAsync(x => x.Id == taskId && x.OrganizationId == session.ActiveOrganizationId);
        if (task is null) return (null, false);
        var isRequester = task.RequesterUserId == session.UserId;
        var assignee = Satisfies(access.Role, task.AssigneeRole, access.RoleName);
        return !isRequester && !assignee ? (null, false) : (task, assignee && !isRequester);
    }

    private static async Task<IResult> Get(Guid taskId, HttpContext context, FlowOpsDbContext db)
    {
        var (task, canDecide) = await Visible(taskId, context, db);
        if (task is null) return Results.NotFound(new { error = "Task not found." });
        return Results.Ok(await Detail(db, task, canDecide, AuthEndpoints.CurrentSession(context)!.UserId));
    }

    private static async Task<object> Detail(FlowOpsDbContext db, ApprovalTask task, bool canDecide, string? userId = null)
    {
        var instance = await db.WorkflowInstances.AsNoTracking().SingleAsync(x => x.Id == task.WorkflowInstanceId);
        var workflow = await db.WorkflowDefinitions.AsNoTracking().Where(x => x.Id == instance.WorkflowDefinitionId).Select(x => x.Name).SingleAsync();
        var version = await db.WorkflowVersions.AsNoTracking().Where(x => x.Id == task.WorkflowVersionId).Select(x => x.VersionNumber).SingleAsync();
        var variables = JsonNode.Parse(instance.ContextJson) as JsonObject;
        var events = await db.ApprovalTaskEvents.AsNoTracking().Where(x => x.TaskId == task.Id).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();
        var ids = events.Select(x => x.ActorUserId).Append(task.RequesterUserId).OfType<string>().Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        string? Name(string? id) => id is not null && names.TryGetValue(id, out var n) ? n : null;
        // The requester can edit and resubmit while changes are requested; the fields come from the form version they submitted.
        var canResubmit = userId is not null && task.RequesterUserId == userId && task.Status == ApprovalTaskStatus.ChangesRequested;
        var fields = canResubmit ? await ResubmitFields(db, instance) : null;
        canResubmit &= fields is not null;
        return new
        {
            task.Id, task.Status, task.AssigneeRole, task.Instructions, task.Revision, task.CreatedAt, task.DecidedAt,
            Workflow = workflow, WorkflowVersion = version, InstanceId = instance.Id, Requester = Name(task.RequesterUserId),
            Request = variables?["request"]?.DeepClone(), Submission = variables?["submission"]?.DeepClone(),
            CanDecide = canDecide && task.Status == ApprovalTaskStatus.Pending, CanResubmit = canResubmit, Fields = canResubmit ? fields : null,
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
        return Results.Ok(await Detail(db, saved, false, session.UserId));
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
        return Results.Ok(await Detail(db, task, false, session.UserId));
    }

    // The fields of the form version the run was started from, with their request keys. Null for runs without a source form.
    private static async Task<List<FormField>?> ResubmitFields(FlowOpsDbContext db, WorkflowInstance instance)
    {
        if (instance.SourceSubmissionId is not { } submissionId) return null;
        var fieldsJson = await (from submission in db.FormSubmissions.AsNoTracking()
                                join publication in db.FormPublications.AsNoTracking() on submission.PublicationId equals publication.Id
                                where submission.Id == submissionId && submission.OrganizationId == instance.OrganizationId
                                select publication.FieldsJson).SingleOrDefaultAsync();
        return fieldsJson is null ? null : FormFieldKeys.Assign(JsonSerializer.Deserialize<List<FormField>>(fieldsJson) ?? []);
    }

    // The requester answers a changes request: revised request.<key> values are validated against the form version they
    // submitted, the run request values are replaced, and the same task reopens as Pending (PHASE5 decision 1).
    private static async Task<IResult> Resubmit(Guid taskId, ResubmitRequest? body, HttpContext context, FlowOpsDbContext db)
    {
        var session = AuthEndpoints.CurrentSession(context)!;
        var (task, _) = await Visible(taskId, context, db, tracked: true);
        if (task is null) return Results.NotFound(new { error = "Task not found." });
        if (task.RequesterUserId != session.UserId)
            return Results.Json(new { error = "Only the requester can resubmit this request.", code = "not_requester" }, statusCode: StatusCodes.Status403Forbidden);
        if (body?.Values is null) return Results.BadRequest(new { error = "Provide the revised values." });
        var comment = CleanComment(body.Comment, false, out var error);
        if (error is not null) return Results.BadRequest(new { error });
        if (body.Revision != task.Revision || task.Status != ApprovalTaskStatus.ChangesRequested)
            return Results.Conflict(new { error = "This request is not waiting for changes any more. Reload it.", code = "task_changed" });

        var instance = await db.WorkflowInstances.SingleAsync(x => x.Id == task.WorkflowInstanceId && x.OrganizationId == task.OrganizationId);
        var fields = await ResubmitFields(db, instance);
        if (fields is null || instance.Status != WorkflowInstanceStatus.Waiting || instance.WaitingNodeId != task.NodeId)
            return Results.Json(new { error = "This request cannot be edited.", code = "not_editable" }, statusCode: StatusCodes.Status409Conflict);
        var variables = JsonNode.Parse(instance.ContextJson) as JsonObject ?? [];
        var current = variables["request"] as JsonObject ?? [];
        if (body.Values.Keys.Any(key => fields.All(field => field.Key != key)))
            return Results.BadRequest(new { error = "Values contain an unknown field." });

        // Files were uploaded with the original submission and cannot be replaced here; they carry over unchanged.
        var values = new Dictionary<Guid, JsonElement>();
        var request = new JsonObject();
        var existingFiles = new HashSet<Guid>();
        foreach (var field in fields)
        {
            var key = field.Key!;
            if (field.Type == "File")
            {
                if (body.Values.TryGetValue(key, out var supplied) && supplied.ValueKind != JsonValueKind.Null
                    && supplied.ToString() != current[key]?.ToString())
                    return Results.BadRequest(new { error = $"{field.Label}: files cannot be changed when resubmitting." });
                if (current[key] is JsonNode file && Guid.TryParse(file.ToString(), out var fileId))
                {
                    existingFiles.Add(fileId);
                    values[field.Id] = JsonSerializer.SerializeToElement(file.ToString());
                    request[key] = file.DeepClone();
                }
                else request[key] = null;
                continue;
            }
            if (!body.Values.TryGetValue(key, out var value)) { request[key] = null; continue; }
            values[field.Id] = value.Clone();
            request[key] = value.ValueKind == JsonValueKind.Null ? null : JsonNode.Parse(value.GetRawText());
        }
        var errors = SubmissionValidation.Validate(fields, values, existingFiles);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        variables["request"] = request;
        instance.ContextJson = variables.ToJsonString();
        instance.Revision++;
        task.Status = ApprovalTaskStatus.Pending;
        task.DecidedByUserId = null;
        task.DecidedAt = null;
        task.Revision++;
        db.ApprovalTaskEvents.Add(new() { TaskId = task.Id, OrganizationId = task.OrganizationId, Kind = ApprovalEventKind.Resubmitted, ActorUserId = session.UserId, Comment = comment });
        AuditService.Append(db, "approval.resubmitted", session.UserId, task.Id.ToString(), task.OrganizationId);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "This request changed in another request. Reload it.", code = "task_changed" });
        }
        db.ChangeTracker.Clear();
        var saved = await db.ApprovalTasks.AsNoTracking().SingleAsync(x => x.Id == taskId);
        return Results.Ok(await Detail(db, saved, false, session.UserId));
    }
}

public sealed record DecisionRequest(string? Decision, string? Comment, int Revision);
public sealed record CommentRequest(string? Comment);
public sealed record ResubmitRequest(Dictionary<string, JsonElement>? Values, string? Comment, int Revision);
