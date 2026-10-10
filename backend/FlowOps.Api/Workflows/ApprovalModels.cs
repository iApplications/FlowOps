namespace FlowOps.Api.Workflows;

// One approval request raised by one Approval node activation (PHASE5). Assignee role and instructions are
// snapshotted from the pinned version, so later drafts never change an open task.
public sealed class ApprovalTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public required string NodeId { get; set; }
    public required string AssigneeRole { get; set; }
    public string? Instructions { get; set; }
    public string Status { get; set; } = ApprovalTaskStatus.Pending;
    public string? RequesterUserId { get; set; }
    public string? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    // Concurrency token: two simultaneous decisions cannot both win.
    public int Revision { get; set; }
    public required string CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class ApprovalTaskStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string ChangesRequested = "ChangesRequested";
    public const string Cancelled = "Cancelled";
}

// Append-only history of a task: durable business history, separate from AuditEvent.
public sealed class ApprovalTaskEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Kind { get; set; }
    public string? ActorUserId { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class ApprovalEventKind
{
    public const string Created = "Created";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string ChangesRequested = "ChangesRequested";
    public const string Resubmitted = "Resubmitted";
    public const string Commented = "Commented";
}
