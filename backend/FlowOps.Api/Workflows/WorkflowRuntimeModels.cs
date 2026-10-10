namespace FlowOps.Api.Workflows;

// Runtime model (PROJECT_RULES 29): a WorkflowInstance is one business run, pinned for life to one
// WorkflowVersionId. Each start or resume of that run is a WorkflowExecution, and each node it runs is a
// WorkflowExecutionStep. Timestamps are UTC DateTime so history can be ordered on every provider.
public sealed class WorkflowInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public string Status { get; set; } = WorkflowInstanceStatus.Running;
    public string ContextJson { get; set; } = "{}";
    public string? WaitingNodeId { get; set; }
    public string? Outcome { get; set; }
    public string Trigger { get; set; } = WorkflowTriggers.Manual;
    public string? StartedByUserId { get; set; }
    public Guid? SourceSubmissionId { get; set; }
    // Manual runs: the caller's Idempotency-Key and a fingerprint of the request it was sent with.
    public string? IdempotencyKey { get; set; }
    public string? RequestFingerprint { get; set; }
    public required string CorrelationId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    // Concurrency token: a resume and any other change to the same instance cannot both win.
    public int Revision { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public static class WorkflowInstanceStatus
{
    public const string Running = "Running";
    public const string Waiting = "Waiting";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class WorkflowExecutionReason
{
    public const string Start = "Start";
    public const string Resume = "Resume";
}

// Execution and step status. An execution that stops at a durable wait ends as Waiting.
public static class WorkflowRunStatus
{
    public const string Running = "Running";
    public const string Waiting = "Waiting";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public sealed class WorkflowExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstanceId { get; set; }
    public Guid OrganizationId { get; set; }
    public int Sequence { get; set; }
    public string Reason { get; set; } = WorkflowExecutionReason.Start;
    public string Status { get; set; } = WorkflowRunStatus.Running;
    public string? TriggeredByUserId { get; set; }
    public required string CorrelationId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class WorkflowExecutionStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExecutionId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid OrganizationId { get; set; }
    public int Sequence { get; set; }
    public required string NodeId { get; set; }
    public required string NodeType { get; set; }
    public string Status { get; set; } = WorkflowRunStatus.Running;
    public int Attempt { get; set; } = 1;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public long DurationMs { get; set; }
    public required string CorrelationId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
