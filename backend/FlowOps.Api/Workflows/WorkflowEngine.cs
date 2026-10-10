using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowOps.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Workflows;

// What a node decided. Continue follows the named output, Wait suspends the instance at this node durably,
// Complete ends the instance with an outcome, and Fail ends it with a structured error.
public enum NodeResultKind { Continue, Wait, Complete, Fail }

public sealed record NodeResult(NodeResultKind Kind, JsonObject? Output = null, string? Handle = null, string? Outcome = null,
    string? ErrorCode = null, string? ErrorMessage = null)
{
    public static NodeResult Next(string handle, JsonObject? output = null) => new(NodeResultKind.Continue, output, handle);
    public static NodeResult Fail(string code, string message) => new(NodeResultKind.Fail, ErrorCode: code, ErrorMessage: message);
}

// ResumeOutput is set only when a waiting node is resumed with the output chosen for it (for example "approved").
public sealed record NodeExecutionContext(WorkflowNode Node, JsonObject Variables, string? ResumeOutput);

// One handler per node type (ARCHITECTURE §8). Handlers are pure decisions over the node and the run's
// variables; the engine owns persistence, routing and failure recording.
public interface IWorkflowNodeHandler
{
    string NodeType { get; }
    NodeResult Execute(NodeExecutionContext context);
}

public sealed class StartNodeHandler : IWorkflowNodeHandler
{
    public string NodeType => WorkflowNodeTypes.Start;
    public NodeResult Execute(NodeExecutionContext context) =>
        context.Variables["request"] is JsonObject
            ? NodeResult.Next("next", new JsonObject { ["trigger"] = context.Node.Config?.Trigger })
            : NodeResult.Fail("context_invalid", "The run has no request values.");
}

public sealed class ConditionNodeHandler : IWorkflowNodeHandler
{
    public string NodeType => WorkflowNodeTypes.Condition;
    public NodeResult Execute(NodeExecutionContext context)
    {
        var result = WorkflowExpression.Evaluate(context.Node.Config?.Expression, context.Variables);
        return result.ErrorCode is null
            ? NodeResult.Next(result.Value ? "true" : "false", new JsonObject { ["result"] = result.Value })
            : NodeResult.Fail(result.ErrorCode, result.ErrorMessage!);
    }
}

// Phase 4 only waits; Phase 5 creates the approval task whose decision resumes the instance.
public sealed class ApprovalNodeHandler : IWorkflowNodeHandler
{
    public string NodeType => WorkflowNodeTypes.Approval;
    public NodeResult Execute(NodeExecutionContext context) => context.ResumeOutput is { } decision
        ? NodeResult.Next(decision, new JsonObject { ["decision"] = decision })
        : new(NodeResultKind.Wait, new JsonObject { ["assigneeRole"] = context.Node.Config?.AssigneeRole });
}

public sealed class EndNodeHandler : IWorkflowNodeHandler
{
    public string NodeType => WorkflowNodeTypes.End;
    public NodeResult Execute(NodeExecutionContext context)
    {
        var outcome = string.IsNullOrWhiteSpace(context.Node.Config?.Outcome) ? "Completed" : context.Node.Config.Outcome.Trim();
        return new(NodeResultKind.Complete, new JsonObject { ["outcome"] = outcome }, Outcome: outcome);
    }
}

public sealed class WorkflowRuntimeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record StartRun(WorkflowDefinition Workflow, WorkflowVersion Version, string Trigger, string? UserId, JsonObject Request,
    string CorrelationId, Guid? SourceSubmissionId = null, string? IdempotencyKey = null, string? RequestFingerprint = null, JsonObject? Submission = null);

// Runs published workflow versions synchronously within the calling request (PHASE4 decision 3). Each
// activation is one WorkflowExecution; every node it runs is persisted as a WorkflowExecutionStep together
// with the instance state in a single save, so a run is never stored half-finished.
public sealed class WorkflowEngine(FlowOpsDbContext db, IEnumerable<IWorkflowNodeHandler> handlers, ILogger<WorkflowEngine> logger)
{
    public const int MaxStepsPerExecution = 200;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private readonly Dictionary<string, IWorkflowNodeHandler> _handlers = handlers.ToDictionary(x => x.NodeType);

    /// <summary>Creates an instance pinned to the published version and runs it until it ends or waits. Throws DbUpdateException when a
    /// concurrent request already started the same idempotent run.</summary>
    public async Task<WorkflowInstance> StartAsync(StartRun run)
    {
        if (run.Version.Status != WorkflowVersionStatus.Published || run.Version.WorkflowDefinitionId != run.Workflow.Id)
            throw new WorkflowRuntimeException("version_not_published", "Only published workflow versions can run.");
        var instance = new WorkflowInstance
        {
            OrganizationId = run.Workflow.OrganizationId, WorkflowDefinitionId = run.Workflow.Id, WorkflowVersionId = run.Version.Id,
            Trigger = run.Trigger, StartedByUserId = run.UserId, SourceSubmissionId = run.SourceSubmissionId, CorrelationId = run.CorrelationId,
            IdempotencyKey = run.IdempotencyKey, RequestFingerprint = run.RequestFingerprint
        };
        var variables = new JsonObject
        {
            ["request"] = run.Request.DeepClone(),
            ["trigger"] = new JsonObject { ["type"] = run.Trigger, ["startedAt"] = instance.StartedAt.ToString("O") }
        };
        if (run.Submission is not null) variables["submission"] = run.Submission.DeepClone();
        db.WorkflowInstances.Add(instance);
        AuditService.Append(db, "workflow.instance_started", run.UserId, instance.Id.ToString(), instance.OrganizationId);
        var graph = run.Version.Graph;
        var start = graph.Nodes.SingleOrDefault(x => x.Type == WorkflowNodeTypes.Start);
        var execution = NewExecution(instance, 1, WorkflowExecutionReason.Start, run.UserId);
        if (start is null) Finish(instance, execution, variables, NodeResult.Fail("start_missing", "The published version has no Start node."), null);
        else Run(instance, execution, graph, start, variables, resumeOutput: null);
        await db.SaveChangesAsync();
        return instance;
    }

    /// <summary>Continues a Waiting instance from its waiting node with the chosen output, as a new execution of the same instance.
    /// Internal in Phase 4: Phase 5 approval tasks call it.</summary>
    public async Task<WorkflowExecution> ResumeAsync(WorkflowInstance instance, string nodeId, string output, string? userId, string correlationId)
    {
        if (instance.Status != WorkflowInstanceStatus.Waiting || instance.WaitingNodeId != nodeId)
            throw new WorkflowRuntimeException("not_waiting", "This run is not waiting at that step.");
        var version = await db.WorkflowVersions.AsNoTracking().SingleAsync(x => x.Id == instance.WorkflowVersionId && x.OrganizationId == instance.OrganizationId);
        var graph = version.Graph;
        var node = graph.Nodes.Single(x => x.Id == nodeId);
        if (!WorkflowGraphValidation.OutputHandles(node.Type).Contains(output))
            throw new WorkflowRuntimeException("output_invalid", $"'{output}' is not an output of {node.Label}.");
        var sequence = await db.WorkflowExecutions.CountAsync(x => x.InstanceId == instance.Id) + 1;
        var execution = NewExecution(instance, sequence, WorkflowExecutionReason.Resume, userId, correlationId);
        instance.Status = WorkflowInstanceStatus.Running;
        instance.WaitingNodeId = null;
        instance.Revision++;
        var variables = JsonNode.Parse(instance.ContextJson) as JsonObject ?? [];
        Run(instance, execution, graph, node, variables, output);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new WorkflowRuntimeException("concurrent_change", "This run changed in another request."); }
        return execution;
    }

    private WorkflowExecution NewExecution(WorkflowInstance instance, int sequence, string reason, string? userId, string? correlationId = null)
    {
        var execution = new WorkflowExecution { InstanceId = instance.Id, OrganizationId = instance.OrganizationId, Sequence = sequence, Reason = reason,
            TriggeredByUserId = userId, CorrelationId = correlationId ?? instance.CorrelationId };
        db.WorkflowExecutions.Add(execution);
        return execution;
    }

    // Follows connections from the given node, one persisted step per node, until End, a durable wait, a failure or the step guard.
    private void Run(WorkflowInstance instance, WorkflowExecution execution, WorkflowGraph graph, WorkflowNode node, JsonObject variables, string? resumeOutput)
    {
        var nodes = graph.Nodes.ToDictionary(x => x.Id);
        var connections = graph.Connections.ToDictionary(x => (x.Source, x.SourceHandle), x => x.Target);
        for (var sequence = 1; ; sequence++)
        {
            if (sequence > MaxStepsPerExecution)
            {
                Finish(instance, execution, variables, NodeResult.Fail("step_limit", $"The run stopped after {MaxStepsPerExecution} steps. Check the workflow for loops."), null);
                return;
            }
            var step = new WorkflowExecutionStep { ExecutionId = execution.Id, InstanceId = instance.Id, OrganizationId = instance.OrganizationId,
                Sequence = sequence, NodeId = node.Id, NodeType = node.Type, CorrelationId = execution.CorrelationId,
                InputJson = JsonSerializer.Serialize(node.Config ?? new(), Json) };
            db.WorkflowExecutionSteps.Add(step);
            var timer = Stopwatch.StartNew();
            NodeResult result;
            try
            {
                result = _handlers.TryGetValue(node.Type, out var handler)
                    ? handler.Execute(new(node, variables, resumeOutput))
                    : NodeResult.Fail("node_unsupported", $"{node.Type} nodes cannot run yet.");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workflow node {NodeId} failed in instance {InstanceId} ({CorrelationId})", node.Id, instance.Id, execution.CorrelationId);
                result = NodeResult.Fail("node_error", "The step failed unexpectedly.");
            }
            resumeOutput = null;
            step.DurationMs = timer.ElapsedMilliseconds;
            step.CompletedAt = DateTime.UtcNow;
            step.OutputJson = result.Output?.ToJsonString();
            if (result.Kind == NodeResultKind.Continue)
            {
                if (!connections.TryGetValue((node.Id, result.Handle!), out var target) || !nodes.TryGetValue(target, out var next))
                    result = NodeResult.Fail("connection_missing", $"{node.Label}: the '{result.Handle}' output is not connected.");
                else
                {
                    step.Status = WorkflowRunStatus.Completed;
                    node = next;
                    continue;
                }
            }
            Finish(instance, execution, variables, result, step);
            return;
        }
    }

    private void Finish(WorkflowInstance instance, WorkflowExecution execution, JsonObject variables, NodeResult result, WorkflowExecutionStep? step)
    {
        var now = DateTime.UtcNow;
        instance.ContextJson = variables.ToJsonString();
        execution.CompletedAt = now;
        switch (result.Kind)
        {
            case NodeResultKind.Wait:
                if (step is not null) { step.Status = WorkflowRunStatus.Waiting; step.CompletedAt = null; }
                execution.Status = WorkflowRunStatus.Waiting;
                instance.Status = WorkflowInstanceStatus.Waiting;
                instance.WaitingNodeId = step?.NodeId;
                break;
            case NodeResultKind.Complete:
                if (step is not null) step.Status = WorkflowRunStatus.Completed;
                execution.Status = WorkflowRunStatus.Completed;
                instance.Status = WorkflowInstanceStatus.Completed;
                instance.Outcome = result.Outcome;
                instance.CompletedAt = now;
                AuditService.Append(db, "workflow.instance_completed", execution.TriggeredByUserId, instance.Id.ToString(), instance.OrganizationId);
                break;
            default:
                if (step is not null) { step.Status = WorkflowRunStatus.Failed; step.ErrorCode = result.ErrorCode; step.ErrorMessage = result.ErrorMessage; }
                execution.Status = WorkflowRunStatus.Failed;
                execution.ErrorCode = instance.ErrorCode = result.ErrorCode;
                execution.ErrorMessage = instance.ErrorMessage = result.ErrorMessage;
                instance.Status = WorkflowInstanceStatus.Failed;
                instance.CompletedAt = now;
                AuditService.Append(db, "workflow.instance_failed", execution.TriggeredByUserId, instance.Id.ToString(), instance.OrganizationId);
                break;
        }
    }
}
